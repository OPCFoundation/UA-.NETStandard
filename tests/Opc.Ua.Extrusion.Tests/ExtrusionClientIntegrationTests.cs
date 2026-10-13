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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.AMB.Server.Assets;
using Opc.Ua.Client;
using Opc.Ua.Client.StateMachines;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Extrusion.Client;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.Server.Hosting;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Uses real TCP, ManagedSession and generated proxies to verify standalone and composed hosting.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ExtrusionClientIntegrationTests
    {
        /// <summary>
        /// Reads, writes, invokes jobs/files and observes real decoded cut events through the public client facade.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        public async Task TypedClientsExerciseAllSiblingsJobsDatasetsAndEventsAsync(bool composed)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            CancellationToken ct = deadline.Token;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            string endpoint = $"opc.tcp://localhost:{port}/Extrusion";
            string root = Path.GetFullPath(Path.Combine(
                "artifacts", "extrusion-tests", "wire", Guid.NewGuid().ToString("N")));
            var configured = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ExtrusionDevice<Extruder.Extruder_InterfaceState>? extruder = null;
            ExtrusionDevice<Filter.Filter_InterfaceState>? filter = null;
            ExtrusionDevice<Cutter.Cutter_InterfaceState>? cutter = null;
            ExtrusionDevice<ExtrusionLine_InterfaceState>? line = null;
            IAssetHandle? asset = null;
            ISystemContext? serverContext = null;
            var datasetProvider = new DatasetProvider();
            HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder();
            hostBuilder.Logging.ClearProviders();
            hostBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
            IOpcUaServerBuilder serverBuilder = hostBuilder.Services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "ExtrusionIntegrationServer";
                options.ApplicationUri = "urn:tests:extrusion:server";
                options.PkiRoot = Path.Combine(root, "server");
                options.IncludeUnsecurePolicyNone = true;
                options.AutoAcceptUntrustedCertificates = true;
                options.EndpointUrls.Add(endpoint);
            });
            if (composed)
            {
                serverBuilder.AddMachinery(options =>
                    {
                        options.Parts = MachineryParts.BuildingBlocks;
                        options.InstanceNamespaceUri = "urn:tests:extrusion:wire:instances";
                    })
                    .AddExtrusionToMachinery();
            }
            else
            {
                serverBuilder.AddExtrusion(options =>
                    options.InstanceNamespaceUri = "urn:tests:extrusion:wire:instances");
            }
            serverBuilder.AddAssetManagement().ConfigureExtrusion(async (context, token) =>
            {
                try
                {
                    serverContext = context.Runtime.SystemContext;
                    ExtrusionDeviceOptions lineOptions = ExtrusionFixture.Options("wire-line");
                    lineOptions.Features.Include("JobGroups").Include("ProductionDatasetManagement");
                    lineOptions.Common.ProductionDatasets = datasetProvider;
                    line = await context.Runtime.CreateLineAsync(lineOptions, cancellationToken: token)
                        .ConfigureAwait(false);
                    ExtrusionDeviceOptions extruderOptions = ExtrusionFixture.Options("wire-extruder");
                    extruderOptions.Features.Include("BarrelId");
                    extruder = await context.Runtime.CreateExtruderAsync(extruderOptions, cancellationToken: token)
                        .ConfigureAwait(false);
                    await extruder.State.ApplyAsync(new ExtrusionOperatingSnapshot(1,
                        MachineryItemStateValue.Executing, MachineryOperationModeValue.Processing,
                        ExtrusionExecutingState.ControlledRun, ExtrusionFixture.Timestamp),
                    [
                        new ExtrusionValue(extruder.Node.IsPresent!.NodeId,
                            new DataValue(Variant.From(true), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                        new ExtrusionValue(extruder.Node.BarrelId!.NodeId,
                            new DataValue(Variant.From("barrel-A"), StatusCodes.Good, ExtrusionFixture.Timestamp))
                    ], token).ConfigureAwait(false);
                    asset = await context.GetRequiredService<IAssetManagement>().RegisterAssetAsync(
                        extruder.AsNode(), cancellationToken: token).ConfigureAwait(false);
                    ExtrusionDeviceOptions filterOptions = ExtrusionFixture.Options("wire-filter");
                    filterOptions.Features.Include("BackflushPressure").WithInstances("FilterPackage_<Nr>", 2);
                    filter = await context.Runtime.CreateFilterAsync(filterOptions, cancellationToken: token)
                        .ConfigureAwait(false);
                    ExtrusionDeviceOptions cutterOptions = ExtrusionFixture.Options("wire-cutter");
                    cutterOptions.Features.Include("ManualCut");
                    cutter = await context.Runtime.CreateCutterAsync(cutterOptions, cancellationToken: token)
                        .ConfigureAwait(false);
                    foreach (int part in s_additionalParts)
                    {
                        await ExtrusionFixture.CreateAsync(context.Runtime, part,
                            ExtrusionFixture.Options("wire-" + part))
                            .ConfigureAwait(false);
                    }
                    configured.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    configured.TrySetException(failure);
                    throw;
                }
            });

            using IHost host = hostBuilder.Build();
            try
            {
                await host.StartAsync(ct).ConfigureAwait(false);
                await configured.Task.WaitAsync(ct).ConfigureAwait(false);
                var services = new ServiceCollection();
                services.AddOpcUa().AddClient(options =>
                {
                    options.ApplicationName = "ExtrusionIntegrationClient";
                    options.ApplicationUri = "urn:tests:extrusion:client";
                    options.PkiRoot = Path.Combine(root, "client");
                    options.AutoAcceptUntrustedCertificates = true;
                    options.Session = new ManagedSessionOptions
                    {
                        SessionName = "ExtrusionIntegrationClient",
                        Endpoint = new ConfiguredEndpoint(null, new EndpointDescription
                        {
                            EndpointUrl = endpoint,
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }, null)
                    };
                }).AddExtrusionClient();
                await using ServiceProvider serviceProvider = services.BuildServiceProvider();
                ExtrusionClient client = await serviceProvider.GetRequiredService<
                    Func<CancellationToken, Task<ExtrusionClient>>>()(ct).ConfigureAwait(false);
                Assert.That(client.Session, Is.InstanceOf<ManagedSession>());
                var session = (ManagedSession)client.Session;
                await using (session.ConfigureAwait(false))
                {
                    var found = new List<ExtrusionDeviceEntry>();
                    await foreach (ExtrusionDeviceEntry entry in client.DiscoverAsync(cancellationToken: ct)
                        .ConfigureAwait(false))
                    {
                        found.Add(entry);
                    }
                    Assert.That(found, Has.Count.EqualTo(11));
                    Assert.That(asset!.NodeId, Is.EqualTo(extruder!.NodeId));
                    var proxy = client.Extruder(extruder.NodeId);
                    ExtrusionRead<bool> present = await proxy.IsPresentVariable().ReadAsync(ct).ConfigureAwait(false);
                    ExtrusionRead<string> barrel = await proxy.BarrelIdVariable().ReadAsync(ct).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(present.HasValue && present.Value, Is.True);
                        Assert.That(present.DataValue.SourceTimestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
                        Assert.That(barrel.Value, Is.EqualTo("barrel-A"));
                    });
                    await proxy.LineIdVariable().WriteAsync("updated-line", ct).ConfigureAwait(false);
                    ExtrusionRead<string> changed = await proxy.LineIdVariable().ReadAsync(ct).ConfigureAwait(false);
                    Assert.That(changed.Value, Is.EqualTo("updated-line"));
                    Assert.That(changed.DataValue.SourceTimestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
                    var itemState = await proxy.ItemStateAsync(ct).ConfigureAwait(false);
                    FiniteStateSnapshot state = await itemState.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false);
                    Assert.That(state.CurrentState.Text, Is.EqualTo("Executing"));
                    var substate = await itemState.GetExtrusionExecutingSubStateAsync(client.Telemetry, ct)
                        .ConfigureAwait(false);
                    Assert.That((await
                        substate!.GetCurrentFiniteStateAsync(ct).ConfigureAwait(false)).CurrentState.Text,
                        Is.EqualTo("ControlledRun"));

                    var filterProxy = client.Filter(filter!.NodeId);
                    await filterProxy.BackflushPressureVariable().WriteAsync(6.5, ct).ConfigureAwait(false);
                    Assert.That((await
                        filterProxy.BackflushPressureVariable().ReadAsync(ct).ConfigureAwait(false)).Value,
                        Is.EqualTo(6.5));
                    EUInformation unit = await filterProxy.BackflushPressureVariable().ReadEngineeringUnitsAsync(ct)
                        .ConfigureAwait(false);
                    Assert.That(unit.UnitId, Is.EqualTo(ExtrusionUnits.Bar.UnitId));
                    int packages = 0;
                    await foreach (Filter.FilterPackageTypeClient _ in filterProxy.EnumerateFilterPackageAsync(ct)
                        .ConfigureAwait(false))
                    {
                        packages++;
                    }
                    Assert.That(packages, Is.EqualTo(2));

                    JobGroupsTypeClient jobs = await client.Line(line!.NodeId).JobsAsync(ct).ConfigureAwait(false);
                    NodeId groupId = await jobs.AddJobGroupAsync("wire-group", "Group", string.Empty, string.Empty,
                        [], 1,
                        DateTimeUtc.MinValue, 0, 0, DateTimeUtc.MinValue, ct).ConfigureAwait(false);
                    var group = new JobGroupTypeClient(session, groupId, client.Telemetry);
                    NodeId jobId = await group.AddJobAsync("wire-job", "Job", "Customer", "Product", "Description",
                        1, 1, [], 10, 2, ct).ConfigureAwait(false);
                    await jobs.StartJobGroupByIdAsync("wire-group", ct).ConfigureAwait(false);
                    await group.StartJobByIdAsync("wire-job", ct).ConfigureAwait(false);
                    var job = new JobTypeClient(session, jobId, client.Telemetry);
                    Assert.That((await job.StatusVariable().ReadAsync(ct).ConfigureAwait(false)).Value,
                        Is.EqualTo(JobStatusEnumeration.JOB_IN_PRODUCTION));
                    await group.InterruptJobByIdAsync("wire-job", ct).ConfigureAwait(false);
                    Assert.That((await job.StatusVariable().ReadAsync(ct).ConfigureAwait(false)).Value,
                        Is.EqualTo(JobStatusEnumeration.JOB_INTERRUPTED));

                    ProductionDatasetClient datasets = await client.Line(line.NodeId).ProductionDatasetsAsync(ct)
                        .ConfigureAwait(false);
                    using var upload = new MemoryStream(new byte[] { 11, 22, 33, 44 });
                    await datasets.UploadAsync(new ProductionDatasetWriteOptionsType
                    {
                        Storage = StorageEnumeration.FILE_SYSTEM,
                        Name = "recipe",
                        Components = []
                    }, upload, DatasetProvider.Information("recipe"), ct).ConfigureAwait(false);
                    using var download = new MemoryStream();
                    ProductionDatasetInformationType info = await datasets.DownloadAsync(new
                        ProductionDatasetReadOptionsType
                    {
                        Storage = StorageEnumeration.FILE_SYSTEM,
                        Name = "recipe"
                    }, download, ct).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(download.ToArray(), Is.EqualTo(new byte[] { 11, 22, 33, 44 }));
                        Assert.That(info.Components, Is.EqualTo((ArrayOf<ushort>)[1, 1]));
                    });
                    await datasets.LoadAsync(StorageEnumeration.PRODUCTION, "recipe", [], ct).ConfigureAwait(false);
                    Assert.That((await datasets.ReadInformationAsync(StorageEnumeration.PRODUCTION, ct)
                        .ConfigureAwait(false)).Name, Is.EqualTo("recipe"));
                    await datasets.SetFrozenAsync(StorageEnumeration.PRODUCTION, true, ct).ConfigureAwait(false);
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await datasets.LoadAsync(StorageEnumeration.PRODUCTION, "recipe", [],
                            ct).ConfigureAwait(false));

                    await using IAsyncEnumerator<ExtrusionEvent> events = client.ObserveEventsAsync(
                        cutter!.NodeId, options: new MonitoredItemOptions
                        {
                            QueueSize = 32,
                            DiscardOldest = false
                        }, cancellationToken: ct).GetAsyncEnumerator(ct);
                    Task<bool> next = events.MoveNextAsync().AsTask();
                    Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
                    while (!manager!.Items.SelectMany(subscription => subscription.MonitoredItems.Items)
                        .Any(item => item.Created))
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(10, ct).ConfigureAwait(false);
                    }
                    Cutter.CuttingProductState product = ExtrusionNodeAccess.Children(
                        serverContext!, cutter.Node.CuttingProducts!).OfType<Cutter.CuttingProductState>().First();
                    await cutter.Cuts!.ApplyAsync(new ExtrusionCutCommit(
                        1, product.Id!.Value, product.Id.Value, 1, 1, ExtrusionFixture.Timestamp),
                            ct).ConfigureAwait(false);
                    Assert.That(await next.WaitAsync(ct).ConfigureAwait(false), Is.True);
                    Assert.That(events.Current.Record, Is.InstanceOf<Cutter.CutEventTypeRecord>());
                    var cut = (Cutter.CutEventTypeRecord)events.Current.Record!;
                    Assert.That(cut.ActualOutput, Is.EqualTo(1ul));
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>Concrete siblings created in addition to the four exercised through retained handles.</summary>
        private static readonly int[] s_additionalParts = [4, 5, 7, 8, 10, 11, 12];
    }
}
