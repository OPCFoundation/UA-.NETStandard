// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PackML;
using Opc.Ua.PackML.Client;
using Opc.Ua.PackML.Server;
using Opc.Ua.Scales.Client;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;
using StandaloneUnit = Opc.Ua.PackML.Server.PackMLUnit;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Exercises real Machinery/Scales and standalone PackML owners in one serving address space.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [Category("PackML")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class ScalesStandalonePackMLCoHostingTests
    {
        /// <summary>
        /// Keeps standalone units discoverable through owner recycling without disrupting embedded scale control.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StandaloneUnitsAndEmbeddedScaleSurviveOwnerRecreationAsync(bool packMLFirst)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = deadline.Token;
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "scales-cohost",
                Guid.NewGuid().ToString("N"));
            string endpointUrl = string.Empty;
            ScaleHandle? scale = null;
            PackMLNodeManager? packMLManager = null;
            StandaloneUnit? initial = null;
            var started = new ServerStartedSignal();
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer<StandardServer>(options =>
            {
                options.ApplicationName = nameof(ScalesStandalonePackMLCoHostingTests);
                options.ApplicationUri = "urn:localhost:scales-packml-cohost";
                options.ProductUri = "urn:localhost:scales-packml-cohost:product";
                options.PkiRoot = Path.Combine(root, "server");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                try
                {
                    endpointUrl = "opc.tcp://localhost:" +
                        ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) +
                        "/ScalesPackML";
                }
                finally
                {
                    listener.Stop();
                }
                options.EndpointUrls.Add(endpointUrl);
            });
            void RegisterPackML()
            {
                builder.AddPackML(options =>
                {
                    options.NamespaceUri = s_standaloneNamespace;
                    options.ConfigureAsync = async (manager, token) =>
                    {
                        packMLManager = manager;
                        initial = await manager.Runtime.AddUnitAsync(
                            new QualifiedName("StandaloneInitial", manager.InstanceNamespaceIndex),
                            new EquipmentProvider(), cancellationToken: token).ConfigureAwait(false);
                    };
                });
            }
            if (packMLFirst)
            {
                RegisterPackML();
            }
            builder.AddMachinery(options =>
                {
                    options.Parts = MachineryParts.BuildingBlocks;
                    options.InstanceNamespaceUri = "urn:scales:cohost:machinery";
                })
                .AddScalesToMachinery()
                .ConfigureScalesFor<MachineryNodeManager>(async (context, token) =>
                {
                    scale = await context.Runtime.CreateScaleAsync(
                        new QualifiedName("EmbeddedScale", context.Runtime.InstanceNamespaceIndex),
                        ScaleKind.Simple,
                        configured => ScalesNodeManagerTests.FullScale(configured, "EmbeddedScale"),
                        token).ConfigureAwait(false);
                });
            if (!packMLFirst)
            {
                RegisterPackML();
            }
            services.AddSingleton<IServerStartupTask>(started);
            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService host = provider.GetServices<IHostedService>().Single();
            try
            {
                await host.StartAsync(ct).ConfigureAwait(false);
                Task startup = host is BackgroundService { ExecuteTask: { } execute }
                    ? await Task.WhenAny(started.Task, execute).WaitAsync(ct).ConfigureAwait(false)
                    : started.Task;
                await startup.WaitAsync(ct).ConfigureAwait(false);
                if (!started.Task.IsCompleted)
                {
                    throw new AssertionException("The background host exited before the server started serving.");
                }
                await started.Task.WaitAsync(ct).ConfigureAwait(false);
                ScaleHandle embedded = scale ?? throw new AssertionException("Scales configuration did not run.");
                PackMLNodeManager owner = packMLManager
                    ?? throw new AssertionException("Standalone PackML configuration did not run.");
                StandaloneUnit first = initial ?? throw new AssertionException("The initial unit was not created.");
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                using var clientFixture = new ClientFixture(telemetry);
                await clientFixture.LoadClientConfigurationAsync(Path.Combine(root, "client")).ConfigureAwait(false);
                EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                    clientFixture.Config, endpointUrl, false, 15000, telemetry, ct).ConfigureAwait(false);
                Assert.That(endpoint, Is.Not.Null);
                await using ManagedSession session = await new ManagedSessionBuilder(clientFixture.Config, telemetry)
                    .UseEndpoint(new ConfiguredEndpoint(
                        null, endpoint!, EndpointConfiguration.Create(clientFixture.Config)))
                    .WithSessionName(nameof(ScalesStandalonePackMLCoHostingTests))
                    .ConnectAsync(ct).ConfigureAwait(false);
                var packMLClient = new PackMLClient(session, telemetry);
                var scalesClient = new ScalesClient(session, telemetry);
                ushort ns = owner.InstanceNamespaceIndex;

                await AssertStandaloneRootsAsync(session, packMLClient, ns, [first.Node.NodeId], ct)
                    .ConfigureAwait(false);
                await AssertEmbeddedControlAsync(scalesClient, embedded.NodeId, ct).ConfigureAwait(false);
                StandaloneUnit late = await owner.Runtime.AddUnitAsync(
                    new QualifiedName("StandaloneLate", ns), new EquipmentProvider(), cancellationToken: ct)
                    .ConfigureAwait(false);
                ArrayOf<NodeId> oldIds = [first.Node.NodeId, late.Node.NodeId];
                await AssertStandaloneRootsAsync(session, packMLClient, ns, oldIds, ct).ConfigureAwait(false);
                // Captured while the owner is live. The predefined Server node belongs to the
                // configuration manager, not to the PackML owner.
                ServerObjectState serverObject = owner.Server.ServerObject;

                INodeManagerLifecycle lifecycle = provider.GetRequiredService<INodeManagerLifecycle>();
                NodeManagerRegistration registration = lifecycle.Registrations.ToList()
                    .Single(entry => ReferenceEquals(entry.NodeManager, owner));
                IAsyncNodeManagerFactory factory = provider.GetServices<OpcUaServerNodeManagerRegistration>()
                    .Select(entry => entry.AsyncFactory)
                    .Single(entry => entry != null &&
                        entry.NamespacesUris.ToList().Contains(s_standaloneNamespace))
                    ?? throw new AssertionException("The AddPackML factory was not registered.");
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, first.Node.NodeId),
                    Is.True);
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, late.Node.NodeId),
                    Is.True);
                LifecycleResult removed = await lifecycle.ApplyAsync(
                    [LifecycleChange.Remove(registration.Slot)], ct).ConfigureAwait(false);
                Assert.That(removed.Issues.Count, Is.Zero);
                Assert.That(removed.CleanupFailure, Is.Null);
                await removed.Retired.WaitAsync(ct).ConfigureAwait(false);
                // Reads raw Server node state once, only after the sequential await of old retirement.
                // Browse hides an edge whose target has no serving owner, so it cannot show this.
                // This is not a concurrency or manager-recreation-policy claim.
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, first.Node.NodeId),
                    Is.False);
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, late.Node.NodeId),
                    Is.False);
                await AssertStandaloneRootsAsync(session, packMLClient, ns, [], ct).ConfigureAwait(false);
                ReadResponse deleted = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = oldIds[0], AttributeId = Attributes.NodeClass },
                        new ReadValueId { NodeId = oldIds[1], AttributeId = Attributes.NodeClass }
                    ], ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(deleted.ResponseHeader.ServiceResult), Is.True);
                Assert.That(deleted.Results.Count, Is.EqualTo(2));
                Assert.That(deleted.Results[0].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
                Assert.That(deleted.Results[1].StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
                await AssertEmbeddedControlAsync(scalesClient, embedded.NodeId, ct).ConfigureAwait(false);

                LifecycleResult recreatedRegistration = await lifecycle.ApplyAsync(
                    [LifecycleChange.Add(factory)], ct).ConfigureAwait(false);
                Assert.That(recreatedRegistration.Issues.Count, Is.Zero);
                Assert.That(recreatedRegistration.CleanupFailure, Is.Null);
                PackMLNodeManager replacement = packMLManager
                    ?? throw new AssertionException("The replacement configuration did not run.");
                StandaloneUnit recreated = initial
                    ?? throw new AssertionException("The initial unit was not recreated.");
                Assert.That(replacement, Is.Not.SameAs(owner));
                Assert.That(replacement.Runtime, Is.Not.SameAs(owner.Runtime));
                Assert.That(replacement.InstanceNamespaceIndex, Is.EqualTo(ns));
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, recreated.Node.NodeId),
                    Is.True);
                await AssertStandaloneRootsAsync(session, packMLClient, ns, [recreated.Node.NodeId], ct)
                    .ConfigureAwait(false);
                StandaloneUnit recreatedLate = await replacement.Runtime.AddUnitAsync(
                    new QualifiedName("StandaloneLate", ns), new EquipmentProvider(), cancellationToken: ct)
                    .ConfigureAwait(false);
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, recreatedLate.Node.NodeId),
                    Is.True);
                await AssertStandaloneRootsAsync(session, packMLClient, ns,
                    [recreated.Node.NodeId, recreatedLate.Node.NodeId], ct).ConfigureAwait(false);
                await AssertEmbeddedControlAsync(scalesClient, embedded.NodeId, ct).ConfigureAwait(false);

                LifecycleResult removedRecreation = await lifecycle.ApplyAsync(
                    [LifecycleChange.Remove(recreatedRegistration.Registrations[0].Slot)], ct)
                    .ConfigureAwait(false);
                Assert.That(removedRecreation.Issues.Count, Is.Zero);
                Assert.That(removedRecreation.CleanupFailure, Is.Null);
                await removedRecreation.Retired.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, recreated.Node.NodeId),
                    Is.False);
                Assert.That(
                    serverObject.ReferenceExists(Ua.ReferenceTypeIds.HasNotifier, false, recreatedLate.Node.NodeId),
                    Is.False);
                await AssertStandaloneRootsAsync(session, packMLClient, ns, [], ct).ConfigureAwait(false);
                await AssertEmbeddedControlAsync(scalesClient, embedded.NodeId, ct).ConfigureAwait(false);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks raw forward and inverse references as well as typed discovery, avoiding cache-only evidence.
        /// </summary>
        private static async Task AssertStandaloneRootsAsync(
            ManagedSession session,
            PackMLClient client,
            ushort ns,
            ArrayOf<NodeId> expected,
            CancellationToken ct)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            });
            ResultSet<ArrayOf<ReferenceDescription>> result = await browser.BrowseAsync(
                [Ua.ObjectIds.ObjectsFolder], ct).ConfigureAwait(false);
            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.That(ServiceResult.IsGood(result.Errors[0]), Is.True);
            Assert.That(result.Results.Count, Is.EqualTo(1));
            NodeId[] roots = result.Results[0].ToList()
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris))
                .Where(id => id.NamespaceIndex == ns).ToArray();
            Assert.That(roots, Is.EquivalentTo(expected.ToList()));
            Assert.That(roots.Distinct().Count(), Is.EqualTo(roots.Length));
            NodeId packMLObjects = ExpandedNodeId.ToNodeId(
                Opc.Ua.PackML.ObjectIds.PackMLObjects, session.NamespaceUris);
            Assert.That(packMLObjects.IsNull, Is.False);
            ResultSet<ArrayOf<ReferenceDescription>> folderResult =
                await browser.BrowseAsync([packMLObjects], ct).ConfigureAwait(false);
            Assert.That(folderResult.Errors.Count, Is.EqualTo(1));
            Assert.That(ServiceResult.IsGood(folderResult.Errors[0]), Is.True);
            Assert.That(folderResult.Results.Count, Is.EqualTo(1));
            NodeId[] folderTargets = folderResult.Results[0].ToList()
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris))
                .Where(id => id.NamespaceIndex == ns).ToArray();
            Assert.That(folderTargets, Is.EquivalentTo(expected.ToList()));
            Assert.That(folderTargets.Distinct().Count(), Is.EqualTo(folderTargets.Length));
            ArrayOf<Opc.Ua.PackML.Client.PackMLUnit> discovered =
                await client.DiscoverAsync(cancellationToken: ct).ConfigureAwait(false);
            Assert.That(discovered.ToList().Where(unit => unit.NodeId.NamespaceIndex == ns)
                .Select(unit => unit.NodeId).ToArray(), Is.EquivalentTo(expected.ToList()));

            var notifiers = new Browser(session, browser.State with
            {
                ReferenceTypeId = Ua.ReferenceTypeIds.HasNotifier
            });
            ResultSet<ArrayOf<ReferenceDescription>> notifierResult =
                await notifiers.BrowseAsync([Ua.ObjectIds.Server], ct).ConfigureAwait(false);
            Assert.That(notifierResult.Errors.Count, Is.EqualTo(1));
            Assert.That(ServiceResult.IsGood(notifierResult.Errors[0]), Is.True);
            Assert.That(notifierResult.Results.Count, Is.EqualTo(1));
            Assert.That(notifierResult.Results[0].ToList()
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris))
                .Where(id => id.NamespaceIndex == ns).ToArray(), Is.EquivalentTo(expected.ToList()));

            var inverseBrowser = new Browser(session, browser.State with
            {
                BrowseDirection = BrowseDirection.Inverse
            });
            for (int index = 0; index < expected.Count; index++)
            {
                ResultSet<ArrayOf<ReferenceDescription>> inverse =
                    await inverseBrowser.BrowseAsync([expected[index]], ct).ConfigureAwait(false);
                Assert.That(inverse.Errors.Count, Is.EqualTo(1));
                Assert.That(ServiceResult.IsGood(inverse.Errors[0]), Is.True);
                Assert.That(inverse.Results.Count, Is.EqualTo(1));
                NodeId[] organizers = inverse.Results[0].ToList()
                    .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris)).ToArray();
                Assert.That(organizers, Has.Length.EqualTo(2));
                Assert.That(organizers, Does.Contain(Ua.ObjectIds.ObjectsFolder));
                Assert.That(organizers, Does.Contain(packMLObjects));
                Assert.That(organizers.Distinct().Count(), Is.EqualTo(organizers.Length));
                PackMLStatusSnapshot status = await client.ReadStatusAsync(expected[index], ct).ConfigureAwait(false);
                Assert.That(status.CurMachSpeed.IsGood, Is.True);
                Assert.That(status.CurMachSpeed.Value, Is.Zero);
            }
        }

        /// <summary>
        /// Uses service calls to exercise the embedded scale controller after each standalone owner change.
        /// </summary>
        private static async Task AssertEmbeddedControlAsync(ScalesClient client, NodeId scale, CancellationToken ct)
        {
            Assert.That(await client.ReadPackMLStateAsync(scale, ct).ConfigureAwait(false),
                Is.EqualTo(PackMLStateNumbers.Idle));
            await client.ExecutePackMLCommandAsync(scale, PackMLCommand.Start, ct).ConfigureAwait(false);
            Assert.That(await client.ReadPackMLStateAsync(scale, ct).ConfigureAwait(false),
                Is.EqualTo(PackMLStateNumbers.Execute));
            await client.ExecutePackMLCommandAsync(scale, PackMLCommand.Stop, ct).ConfigureAwait(false);
            Assert.That(await client.ReadPackMLStateAsync(scale, ct).ConfigureAwait(false),
                Is.EqualTo(PackMLStateNumbers.Stopped));
            await client.ExecutePackMLCommandAsync(scale, PackMLCommand.Reset, ct).ConfigureAwait(false);
            Assert.That(await client.ReadPackMLStateAsync(scale, ct).ConfigureAwait(false),
                Is.EqualTo(PackMLStateNumbers.Idle));
        }

        /// <summary>
        /// Supplies nonphysical equipment readings without authorizing hardware operation.
        /// </summary>
        private sealed class EquipmentProvider : IPackMLEquipmentProvider
        {
            /// <inheritdoc/>
            public ValueTask<ServiceResult> AuthorizeAsync(
                ISystemContext context, PackMLRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <inheritdoc/>
            public ValueTask<ServiceResult> ExecuteAsync(
                PackMLRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <inheritdoc/>
            public ValueTask<PackMLEquipmentSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<PackMLEquipmentSnapshot>(new PackMLEquipmentSnapshot
                {
                    CurrentMode = 1,
                    StatusCode = StatusCodes.Good,
                    SourceTimestamp = new DateTimeUtc(2026, 1, 2, 3, 4, 5)
                });
            }
        }

        /// <summary>
        /// Waits for serving readiness rather than only address-space configuration.
        /// </summary>
        private sealed class ServerStartedSignal : IServerStartupTask
        {
            /// <summary>
            /// Gets the serving-readiness task.
            /// </summary>
            internal Task Task => m_started.Task;

            /// <inheritdoc/>
            public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
            {
                m_started.TrySetResult(true);
                return default;
            }

            /// <summary>The serving-readiness signal.</summary>
            private readonly TaskCompletionSource<bool> m_started =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>The separate standalone application namespace.</summary>
        private const string s_standaloneNamespace = "urn:scales:cohost:standalone-packml";
    }
}
