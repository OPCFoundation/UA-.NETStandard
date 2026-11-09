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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies standalone and composed DI hosting execute the same actual native builder and use one namespace owner.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [NonParallelizable]
    public sealed class GmsSystemHostingTests
    {
        /// <summary>
        /// Both registrations host independent live native roots on one actual Machinery/DI manager.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DiHostingRunsNativeConfiguratorsOnOneRealOwnerAsync(bool shared)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
                options.ApplicationName = "GmsSystemHostingTests");
            const string instanceNamespace = "urn:opcfoundation:gms:di-tests";
            if (shared)
            {
                builder.AddMachinery(options =>
                {
                    options.Parts = MachineryParts.BuildingBlocks | MachineryParts.Result;
                    options.InstanceNamespaceUri = instanceNamespace;
                }).AddGmsToMachinery().AddGmsToMachinery();
            }
            else
            {
                builder.AddGms(options => options.InstanceNamespaceUri = instanceNamespace);
            }
            var handles = new List<GmsHandle>();
            builder.ConfigureGms(async (context, cancellationToken) =>
            {
                for (int i = 0; i < 2; i++)
                {
                    GmsHandle handle = await GmsSystemTests.Builder(context, "DiGms" + i,
                        new InMemoryGmsProvider(new GmsSystemSnapshot()))
                        .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                        .BuildAsync(cancellationToken).ConfigureAwait(false);
                    handles.Add(handle);
                }
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            MachineryNodeManagerFactory owner = provider.GetRequiredService<MachineryNodeManagerFactory>();
            Assert.That(services.Count(value => value.ServiceType == typeof(DiAddressSpaceOwnership)), Is.EqualTo(1));
            Assert.That(services.Count(value => value.ServiceType == typeof(MachineryNodeManagerFactory)),
                Is.EqualTo(1));
            Assert.That(provider.GetServices<IMachineryModelProvider>().OfType<GmsModelProvider>().Count(),
                Is.EqualTo(1));
            Assert.That(owner.NamespacesUris.ToArray(), Does.Contain(GmsModel.NamespaceUri));
            Assert.That(owner.NamespacesUris.ToArray(), Does.Not.Contain(Opc.Ua.MachineTool.WMTP.Namespaces.WMTP));
            Assert.That(services.Any(value => value.ServiceType == typeof(IGmsProvider)), Is.False);
            Assert.That(services.Any(value => value.ServiceType == typeof(IGmsResultStore)), Is.False);
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(owner);
                return server;
            })
            { AutoAccept = true, SecurityNone = true };
            string root = Path.GetFullPath(Path.Combine(
                Environment.GetEnvironmentVariable("OPCUA_GMS_TEST_ROOT") ?? Environment.CurrentDirectory,
                ".gms-system", "di-" + Guid.NewGuid().ToString("N")[..8]));
            try
            {
                await fixture.LoadConfigurationAsync(root).ConfigureAwait(false);
                await fixture.StartAsync().ConfigureAwait(false);
                Assert.That(handles, Has.Count.EqualTo(2));
                Assert.That(handles[0].NodeId, Is.Not.EqualTo(handles[1].NodeId));
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                await using var client = new ClientFixture(telemetry);
                await client.LoadClientConfigurationAsync(root, "GmsDiClient").ConfigureAwait(false);
                using ISession session = await client.ConnectAsync(
                    new Uri(fixture.Config.ServerConfiguration!.BaseAddresses[0]), SecurityPolicies.None)
                    .ConfigureAwait(false);
                await using var gms = new GmsClient(session, telemetry);
                ArrayOf<GmsDeviceEntry> devices = await gms.DiscoverAsync().ConfigureAwait(false);
                Assert.That(devices.Count, Is.EqualTo(2));
                Assert.That((await gms.ReadSnapshotAsync(handles[0].NodeId).ConfigureAwait(false))
                    .Identification.Manufacturer.IsPresent, Is.True);
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Invalid owner configuration fails instead of registering nonworking mandatory result methods.
        /// </summary>
        [Test]
        public void CompositionRequiresAnExistingOwnerWithResultSupport()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            Assert.Throws<InvalidOperationException>(() => builder.AddGmsToMachinery());
            builder.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            builder.AddGmsToMachinery();
            using ServiceProvider provider = services.BuildServiceProvider();
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                provider.GetRequiredService<MachineryNodeManagerFactory>())!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// Disposing one factory facade leaves the real container-cached session available to other consumers.
        /// </summary>
        [Test]
        public async Task ClientFactoryBorrowsContainerSessionWithoutDisconnectingOtherConsumersAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using GmsHandle handle = await GmsSystemTests.Builder(GmsSystemTests.Context(fixture), "FactoryGms",
                new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using ServiceProvider provider = await CreateClientProviderAsync(fixture, telemetry)
                .ConfigureAwait(false);
            IGmsClientFactory factory = provider.GetRequiredService<IGmsClientFactory>();
            Func<CancellationToken, Task<ManagedSession>> acquire =
                provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession shared = await acquire(CancellationToken.None).ConfigureAwait(false);
            int closing = 0;
            shared.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Closing)
                {
                    Interlocked.Increment(ref closing);
                }
            };
            await using GmsClient first = await factory.CreateAsync().ConfigureAwait(false);
            await using GmsClient second = await factory.CreateAsync().ConfigureAwait(false);
            Assert.That(first.Session, Is.SameAs(shared));
            Assert.That(second.Session, Is.SameAs(shared));
            Assert.That(await first.DiscoverAsync().ConfigureAwait(false), Has.Count.EqualTo(1));
            Assert.That(shared.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
            int before = subscriptions!.Count;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Task<MachineToolEventObservation> pending = first.ObserveEventsAsync(
                handle.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromMinutes(1) },
                deadline.Token).AsTask();
            while (subscriptions.Count == before)
            {
                await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            }

            await first.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);

            await Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            Assert.That(subscriptions.Count, Is.EqualTo(before));
            Assert.That(shared.Disposed, Is.False);
            Assert.That(shared.Connected, Is.True);
            Assert.That(closing, Is.Zero);
            Assert.That(await acquire(CancellationToken.None).ConfigureAwait(false), Is.SameAs(shared));
            Assert.That(await second.DiscoverAsync().ConfigureAwait(false), Has.Count.EqualTo(1));
            DataValue value = await shared.ReadValueAsync(Ua.VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(() => first.DiscoverAsync().AsTask(), Throws.InstanceOf<ObjectDisposedException>());

            await second.DisposeAsync().ConfigureAwait(false);
            Assert.That(shared.Connected, Is.True);
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(shared.Disposed, Is.True, "Only the container owns the cached session.");
            Assert.That(closing, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancellation after cached-session acquisition does not close or dispose another consumer's session.
        /// </summary>
        [Test]
        public async Task ClientFactoryCancellationAfterAcquisitionPreservesSharedSessionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using ServiceProvider provider = await CreateClientProviderAsync(fixture, telemetry)
                .ConfigureAwait(false);
            Func<CancellationToken, Task<ManagedSession>> acquire =
                provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession shared = await acquire(CancellationToken.None).ConfigureAwait(false);
            await using GmsClient other = await provider.GetRequiredService<IGmsClientFactory>()
                .CreateAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            int closing = 0;
            shared.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Closing)
                {
                    Interlocked.Increment(ref closing);
                }
            };
            int acquisitions = 0;
            var factory = new GmsClientFactory(async token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                ManagedSession cached = await acquire(token).ConfigureAwait(false);
                Assert.That(cached, Is.SameAs(shared));
                acquisitions++;
                await cancellation.CancelAsync().ConfigureAwait(false);
                return cached;
            }, telemetry);

            OperationCanceledException error = (await Assert.ThrowsAsync<OperationCanceledException>(
                () => factory.CreateAsync(cancellation.Token)))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(acquisitions, Is.EqualTo(1));
            Assert.That(closing, Is.Zero);
            Assert.That(shared.Disposed, Is.False);
            Assert.That(shared.Connected, Is.True);
            Assert.That(other.Session, Is.SameAs(shared));
            DataValue value = await other.Session.ReadValueAsync(Ua.VariableIds.Server_ServerStatus_CurrentTime)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(await acquire(CancellationToken.None).ConfigureAwait(false), Is.SameAs(shared));
        }

        /// <summary>
        /// Failed facade construction leaves disposal of the borrowed session with its container.
        /// </summary>
        [Test]
        public async Task ClientFactoryCreationFailureDoesNotDisposeBorrowedSessionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using ServiceProvider provider = await CreateClientProviderAsync(fixture, telemetry)
                .ConfigureAwait(false);
            Func<CancellationToken, Task<ManagedSession>> acquire =
                provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            ManagedSession shared = await acquire(CancellationToken.None).ConfigureAwait(false);
            Assert.That(await shared.CloseAsync(CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.Good));
            Assert.That(shared.Disposed, Is.False);

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                () => provider.GetRequiredService<IGmsClientFactory>().CreateAsync()))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(shared.Disposed, Is.False);
            Assert.That(await acquire(CancellationToken.None).ConfigureAwait(false), Is.SameAs(shared));
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(shared.Disposed, Is.True);
        }

        /// <summary>
        /// A pre-cancelled request never invokes the session accessor.
        /// </summary>
        [Test]
        public async Task ClientFactoryRejectsCancellationBeforeAcquisition()
        {
            bool acquired = false;
            var factory = new GmsClientFactory(_ =>
            {
                acquired = true;
                throw new InvalidOperationException("The cancelled request must not acquire a session.");
            }, NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OperationCanceledException error = (await Assert.ThrowsAsync<OperationCanceledException>(
                () => factory.CreateAsync(cancellation.Token)))!;

            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(acquired, Is.False);
        }

        /// <summary>
        /// Invalid factory bounds fail before any shared connection can be acquired.
        /// </summary>
        [TestCase(0, 1, 1)]
        [TestCase(1, 0, 1)]
        [TestCase(1, 1, 0)]
        public void ClientFactoryRejectsInvalidOptionsBeforeAcquisition(int elements, int references, int payload)
        {
            bool acquired = false;
            Assert.That(() => new GmsClientFactory(_ =>
            {
                acquired = true;
                throw new InvalidOperationException("Invalid options must not acquire a session.");
            }, NUnitTelemetryContext.Create(), new GmsClientOptions
            {
                MaximumElements = elements,
                MaximumReferences = references,
                MaximumPayloadBytes = payload
            }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(acquired, Is.False);
        }

        /// <summary>
        /// Explicit ownership of an independently created session still closes that session exactly once.
        /// </summary>
        [Test]
        public async Task ExplicitOwnedFacadeStillClosesItsIndependentSessionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
            var independent = (ManagedSession)session;
            int closing = 0;
            independent.ConnectionStateChanged += (_, change) =>
            {
                if (change.NewState == ConnectionState.Closing)
                {
                    Interlocked.Increment(ref closing);
                }
            };
            ConstructorInfo constructor = typeof(GmsClient).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    typeof(ISession),
                    typeof(ITelemetryContext),
                    typeof(bool),
                    typeof(GmsClientOptions),
                    typeof(TimeProvider)
                ],
                modifiers: null)!;
            await using var client = (GmsClient)constructor.Invoke(
                [independent, NUnitTelemetryContext.Create(), true, null, null]);

            await client.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);

            Assert.That(independent.Disposed, Is.True);
            Assert.That(independent.Connected, Is.False);
            Assert.That(closing, Is.EqualTo(1));
        }

        private static async Task<ServiceProvider> CreateClientProviderAsync(
            GmsResultServerFixture fixture,
            ITelemetryContext telemetry)
        {
            ClientFixture transport = await fixture.CreateClientFixtureAsync(telemetry).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await transport.GetEndpointAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddSingleton(telemetry);
            services.AddOpcUa().AddClient(options =>
            {
                options.Configuration = transport.Config;
                options.Session = new ManagedSessionOptions
                {
                    Endpoint = endpoint,
                    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance
                };
            }).AddGmsClient().AddGmsClient();
            return services.BuildServiceProvider();
        }
    }
}
