// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="LoadDirectionStartupTask"/>: constructor guards, the null-server and
    /// missing-ServerUri no-op paths, and the happy path that activates the <see cref="ServerLoadDirector"/>
    /// with the collaborators built from the populated server message context.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class LoadDirectionStartupTaskTests
    {
        private const string BalancingUrl = "opc.tcp://balance:4840";
        private const string LocalServerUri = "urn:A";
        private const string PeerServerUri = "urn:B";

        [Test]
        public void ConstructorThrowsOnNullStore()
        {
            Assert.That(
                () => new LoadDirectionStartupTask(
                    null!,
                    NullRecordProtector.Instance,
                    new LoadDirectionOptions(),
                    CreateDirector(),
                    new FakeTimeProvider()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullProtector()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(
                () => new LoadDirectionStartupTask(
                    store,
                    null!,
                    new LoadDirectionOptions(),
                    CreateDirector(),
                    new FakeTimeProvider()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullOptions()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(
                () => new LoadDirectionStartupTask(
                    store,
                    NullRecordProtector.Instance,
                    null!,
                    CreateDirector(),
                    new FakeTimeProvider()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullDirector()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(
                () => new LoadDirectionStartupTask(
                    store,
                    NullRecordProtector.Instance,
                    new LoadDirectionOptions(),
                    null!,
                    new FakeTimeProvider()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorThrowsOnNullTimeProvider()
        {
            using var store = new InMemorySharedKeyValueStore();

            Assert.That(
                () => new LoadDirectionStartupTask(
                    store,
                    NullRecordProtector.Instance,
                    new LoadDirectionOptions(),
                    CreateDirector(),
                    null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void OnServerStartedThrowsOnNullServer()
        {
            using var store = new InMemorySharedKeyValueStore();
            var task = new LoadDirectionStartupTask(
                store,
                NullRecordProtector.Instance,
                new LoadDirectionOptions(),
                CreateDirector(),
                new FakeTimeProvider());

            Assert.That(
                async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedNoOpsWhenLocalServerUriMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            ServerLoadDirector director = CreateDirector(localServiceLevel: 255, localLoad: 200);
            var task = new LoadDirectionStartupTask(
                store, NullRecordProtector.Instance, options, director, new FakeTimeProvider());
            Mock<IServerInternal> server = CreateServer(hasServerUri: false, out _);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            // The director was never configured, so it fails safe to the local Server.
            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(
                BalancingUrl, LocalEndpoints(LocalServerUri)).ConfigureAwait(false);
            Assert.That(redirect, Is.False, "an unconfigured director must serve locally");
        }

        [Test]
        public async Task OnServerStartedConfiguresDirectorForRedirectionAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions
            {
                BalancingEndpointUrl = BalancingUrl,
                PeerApplicationUris = [PeerServerUri]
            };
            var time = new FakeTimeProvider();

            // A healthier, idle peer is available in the shared store.
            await PublishPeerAsync(store, context, options, time, PeerServerUri, serviceLevel: 255, load: 0).ConfigureAwait(false);

            ServerLoadDirector director = CreateDirector(localServiceLevel: 255, localLoad: 200);
            var task = new LoadDirectionStartupTask(store, NullRecordProtector.Instance, options, director, time);
            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out _, context);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            (bool redirect, ArrayOf<EndpointDescription> endpoints) =
                await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints(LocalServerUri)).ConfigureAwait(false);

            Assert.That(redirect, Is.True, "the configured director redirects to the healthier idle peer");
            Assert.That(endpoints.Count, Is.EqualTo(1));
            Assert.That(endpoints[0]?.Server?.ApplicationUri, Is.EqualTo(PeerServerUri));
        }

        [Test]
        public async Task IdleHostedPeerRefreshesHealthAndCurrentEndpointsUntilStoppedAsync()
        {
            const string updatedUrl = "opc.tcp://renewed:4840";
            using var store = new InMemorySharedKeyValueStore();
            var time = new FakeTimeProvider();
            var options = new LoadDirectionOptions
            {
                PeerApplicationUris = [LocalServerUri],
                StalenessWindow = TimeSpan.FromSeconds(10)
            };
            ArrayOf<EndpointDescription> snapshot = LocalEndpoints(LocalServerUri);
            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out IServiceMessageContext context,
                endpointSnapshot: () => snapshot);
            var directory = new SharedPeerEndpointDirectory(store, context, NullRecordProtector.Instance,
                options, time);
            var peerView = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            ServerLoadDirector director = CreateDirector();
            var startupTask = new LoadDirectionStartupTask(store, NullRecordProtector.Instance, options,
                director, time, options.PeerApplicationUris, useHostedPublishing: true);
            await using var publisher = new PeerDirectionPublishStartupTask(store, NullRecordProtector.Instance,
                options, new ConstantServiceLevelProvider(255), new ConstantLoadWeightProvider(), time);
            await startupTask.OnServerStartedAsync(server.Object).ConfigureAwait(false);
            await publisher.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            ArrayOf<EndpointDescription> initial = await directory.GetEndpointsAsync(LocalServerUri)
                .ConfigureAwait(false);
            Assert.That(initial.Count, Is.EqualTo(1));
            Assert.That(initial[0].EndpointUrl, Is.Not.EqualTo(updatedUrl));

            snapshot = LocalEndpoints(LocalServerUri);
            snapshot[0].EndpointUrl = updatedUrl;
            time.Advance(TimeSpan.FromSeconds(6));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                ArrayOf<EndpointDescription> current = await directory.GetEndpointsAsync(LocalServerUri, timeout.Token)
                    .ConfigureAwait(false);
                if (current.Count == 1 && current[0].EndpointUrl == updatedUrl)
                {
                    break;
                }
                await Task.Delay(10, timeout.Token).ConfigureAwait(false);
            }

            time.Advance(TimeSpan.FromSeconds(5));
            Assert.That((await peerView.GetPeersAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            await director.TryGetDirectedEndpointsAsync("opc.tcp://normal:4840", LocalEndpoints(LocalServerUri))
                .ConfigureAwait(false);
            ArrayOf<EndpointDescription> fresh = await directory.GetEndpointsAsync(LocalServerUri)
                .ConfigureAwait(false);
            Assert.That(fresh.Count, Is.EqualTo(1));
            Assert.That(fresh[0].EndpointUrl, Is.EqualTo(updatedUrl));

            await publisher.DisposeAsync().ConfigureAwait(false);
            time.Advance(options.StalenessWindow + TimeSpan.FromTicks(1));
            Assert.That((await peerView.GetPeersAsync().ConfigureAwait(false)).Count, Is.Zero);
            Assert.That((await directory.GetEndpointsAsync(LocalServerUri).ConfigureAwait(false)).Count, Is.Zero);
        }

        [TestCase(true, false, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        public async Task RegisteredMembershipOrExplicitLocalFallbackAsync(
            bool registerStaticPeer, bool emptyOverride, bool expectedRedirect)
        {
            using var store = new InMemorySharedKeyValueStore();
            var clock = new FakeTimeProvider();
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton<ISharedKeyValueStore>(store);
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddSingleton<IServiceLevelProvider>(new ConstantServiceLevelProvider(100));
            if (registerStaticPeer)
            {
                builder.AddServerRedundancy(options =>
                    options.AddRedundantPeer(PeerServerUri, ["opc.tcp://peer:4840"]));
            }
            builder.UseServerLoadDirection(options =>
            {
                options.BalancingEndpointUrl = BalancingUrl;
                if (emptyOverride)
                {
                    options.PeerApplicationUris = ArrayOf<string>.Empty;
                }
            });
            await using ServiceProvider services = builder.Services.BuildServiceProvider();
            LoadDirectionOptions options = services.GetRequiredService<LoadDirectionOptions>();
            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out IServiceMessageContext context);
            await PublishPeerAsync(store, context, options, clock, PeerServerUri, 255, 0).ConfigureAwait(false);
            LoadDirectionStartupTask task = services.GetServices<IServerStartupTask>()
                .OfType<LoadDirectionStartupTask>()
                .Single();
            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            (bool redirect, ArrayOf<EndpointDescription> endpoints) = await services
                .GetRequiredService<ServerLoadDirector>()
                .TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints(LocalServerUri)).ConfigureAwait(false);

            Assert.That(redirect, Is.EqualTo(expectedRedirect));
            Assert.That(endpoints.Count, Is.EqualTo(expectedRedirect ? 1 : 0));
        }

        private static ServerLoadDirector CreateDirector(byte localServiceLevel = 255, byte localLoad = 0)
        {
            return new ServerLoadDirector(
                new ConstantServiceLevelProvider(localServiceLevel),
                new ConstantLoadWeightProvider(localLoad),
                new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl });
        }

        private static Mock<IServerInternal> CreateServer(
            bool hasServerUri,
            out IServiceMessageContext context,
            IServiceMessageContext? sharedContext = null,
            Func<ArrayOf<EndpointDescription>>? endpointSnapshot = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            context = sharedContext ?? ServiceMessageContext.CreateEmpty(telemetry);
            var serverUris = new StringTable();
            if (hasServerUri)
            {
                serverUris.Append(LocalServerUri);
            }

            var server = new Mock<IServerInternal>();
            if (endpointSnapshot is not null)
            {
                server.As<IServerEndpointRegistryProvider>().Setup(s => s.ServerEndpoints).Returns(endpointSnapshot);
            }
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.MessageContext).Returns(context);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private static async Task PublishPeerAsync(
            InMemorySharedKeyValueStore store,
            IServiceMessageContext context,
            LoadDirectionOptions options,
            FakeTimeProvider time,
            string serverUri,
            byte serviceLevel,
            byte load)
        {
            var directionPublisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, serverUri);
            await directionPublisher.PublishServiceLevelAsync(serviceLevel).ConfigureAwait(false);
            await directionPublisher.PublishLoadWeightAsync(load).ConfigureAwait(false);

            var endpointPublisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, serverUri, time);
            await endpointPublisher.PublishAsync(LocalEndpoints(serverUri)).ConfigureAwait(false);
        }

        private static ArrayOf<EndpointDescription> LocalEndpoints(string serverUri)
        {
            return
            [
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://" + serverUri + ":4840",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    Server = new ApplicationDescription
                    {
                        ApplicationUri = serverUri,
                        ApplicationType = ApplicationType.Server
                    }
                }
            ];
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }
    }
}
