/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration tests for <see cref="ServerLoadDirector"/> using an in-memory shared store shared by two Servers.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class ServerLoadDirectorTests
    {
        private const string BalancingUrl = "opc.tcp://balance:4840";

        [Test]
        public async Task RedirectsToLessLoadedPeerOnBalancingUrlAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            var time = new FakeTimeProvider();

            await PublishPeerAsync(store, context, options, time, "urn:B", serviceLevel: 255, load: 0).ConfigureAwait(false);

            ServerLoadDirector director = CreateDirector(
                store, context, options, time, "urn:A", localServiceLevel: 255, localLoad: 200);

            (bool redirect, ArrayOf<EndpointDescription> endpoints) =
                await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints("urn:A")).ConfigureAwait(false);

            Assert.That(redirect, Is.True);
            EndpointDescription[] result = endpoints.ToArray()!;
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Server.ApplicationUri, Is.EqualTo("urn:B"));
        }

        [Test]
        public async Task ServesLocalOnNormalUrlAndPublishesOwnEndpointsAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            var time = new FakeTimeProvider();

            ServerLoadDirector director = CreateDirector(
                store, context, options, time, "urn:A", localServiceLevel: 255, localLoad: 0);

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(
                "opc.tcp://a:4840", LocalEndpoints("urn:A")).ConfigureAwait(false);

            Assert.That(redirect, Is.False, "a normal discovery request is served locally");

            var directory = new SharedPeerEndpointDirectory(
                store, context, NullRecordProtector.Instance, options);
            EndpointDescription[] published = (await directory.GetEndpointsAsync("urn:A").ConfigureAwait(false)).ToArray()!;
            Assert.That(published, Has.Length.EqualTo(1), "the local endpoints are published for peers");
            Assert.That(published[0].Server.ApplicationUri, Is.EqualTo("urn:A"));
        }

        [Test]
        public async Task RedirectsToHealthierActivePeerRegardlessOfLoadAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            var time = new FakeTimeProvider();

            await PublishPeerAsync(store, context, options, time, "urn:B", serviceLevel: 255, load: 250).ConfigureAwait(false);

            // Local Server is a cold standby (NoData).
            ServerLoadDirector director = CreateDirector(
                store, context, options, time, "urn:A", localServiceLevel: ServiceLevels.NoData, localLoad: 0);

            (bool redirect, ArrayOf<EndpointDescription> endpoints) =
                await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints("urn:A")).ConfigureAwait(false);

            Assert.That(redirect, Is.True);
            Assert.That(endpoints.ToArray()![0].Server.ApplicationUri, Is.EqualTo("urn:B"));
        }

        [Test]
        public async Task ServesLocalWhenNotConfiguredAsync()
        {
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            var director = new ServerLoadDirector(
                new ConstantServiceLevelProvider(255), new ConstantLoadWeightProvider(0), options);

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints("urn:A")).ConfigureAwait(false);

            Assert.That(redirect, Is.False, "an unconfigured director never redirects");
        }

        [Test]
        public async Task ServesLocalWhenTargetEndpointsMissingAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            var time = new FakeTimeProvider();

            // B is the best target by health/load but never published its endpoints.
            var directionPublisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:B");
            await directionPublisher.PublishServiceLevelAsync(255).ConfigureAwait(false);
            await directionPublisher.PublishLoadWeightAsync(0).ConfigureAwait(false);

            ServerLoadDirector director = CreateDirector(
                store, context, options, time, "urn:A", localServiceLevel: 255, localLoad: 200);

            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints("urn:A")).ConfigureAwait(false);

            Assert.That(redirect, Is.False, "without the target's endpoints the request fails safe to the local Server");
        }

        private static ServerLoadDirector CreateDirector(
            InMemorySharedKeyValueStore store,
            IServiceMessageContext context,
            LoadDirectionOptions options,
            FakeTimeProvider time,
            string localServerUri,
            byte localServiceLevel,
            byte localLoad)
        {
            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, time);
            var policy = new BandedServerDirectionPolicy(view, options, _ => 0);
            var directory = new SharedPeerEndpointDirectory(store, context, NullRecordProtector.Instance, options);
            var endpointPublisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, localServerUri);

            var director = new ServerLoadDirector(
                new ConstantServiceLevelProvider(localServiceLevel),
                new ConstantLoadWeightProvider(localLoad),
                options);
            director.Configure(policy, directory, endpointPublisher, localServerUri);
            return director;
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
                store, context, NullRecordProtector.Instance, options, serverUri);
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
