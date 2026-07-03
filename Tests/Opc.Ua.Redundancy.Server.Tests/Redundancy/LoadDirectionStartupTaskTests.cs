/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
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
                async () => await task.OnServerStartedAsync(null!),
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

            await task.OnServerStartedAsync(server.Object);

            // The director was never configured, so it fails safe to the local Server.
            (bool redirect, _) = await director.TryGetDirectedEndpointsAsync(
                BalancingUrl, LocalEndpoints(LocalServerUri));
            Assert.That(redirect, Is.False, "an unconfigured director must serve locally");
        }

        [Test]
        public async Task OnServerStartedConfiguresDirectorForRedirectionAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { BalancingEndpointUrl = BalancingUrl };
            var time = new FakeTimeProvider();

            // A healthier, idle peer is available in the shared store.
            await PublishPeerAsync(store, context, options, time, PeerServerUri, serviceLevel: 255, load: 0);

            ServerLoadDirector director = CreateDirector(localServiceLevel: 255, localLoad: 200);
            var task = new LoadDirectionStartupTask(store, NullRecordProtector.Instance, options, director, time);
            Mock<IServerInternal> server = CreateServer(hasServerUri: true, out _, context);

            await task.OnServerStartedAsync(server.Object);

            (bool redirect, ArrayOf<EndpointDescription> endpoints) =
                await director.TryGetDirectedEndpointsAsync(BalancingUrl, LocalEndpoints(LocalServerUri));

            Assert.That(redirect, Is.True, "the configured director redirects to the healthier idle peer");
            Assert.That(endpoints.Count, Is.EqualTo(1));
            Assert.That(endpoints[0]?.Server?.ApplicationUri, Is.EqualTo(PeerServerUri));
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
            IServiceMessageContext? sharedContext = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            context = sharedContext ?? ServiceMessageContext.CreateEmpty(telemetry);
            var serverUris = new StringTable();
            if (hasServerUri)
            {
                serverUris.Append(LocalServerUri);
            }

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.MessageContext).Returns(context);
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
            await directionPublisher.PublishServiceLevelAsync(serviceLevel);
            await directionPublisher.PublishLoadWeightAsync(load);

            var endpointPublisher = new SharedPeerEndpointPublisher(
                store, context, NullRecordProtector.Instance, options, serverUri);
            await endpointPublisher.PublishAsync(LocalEndpoints(serverUri));
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
