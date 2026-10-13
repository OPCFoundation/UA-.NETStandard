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
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

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
            }) { AutoAccept = true, SecurityNone = true };
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
        /// Direct and injectable client factories own only the managed session that they explicitly open.
        /// </summary>
        [Test]
        public async Task ClientFactoryUsesExistingManagedSessionAndDisposesItsOwnConnectionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using GmsHandle handle = await GmsSystemTests.Builder(GmsSystemTests.Context(fixture), "FactoryGms",
                new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ManagedSession? opened = null;
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(_ => { }).AddGmsClient().AddGmsClient();
            services.AddSingleton(telemetry);
            services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(async _ =>
            {
                ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
                opened = session as ManagedSession ??
                    throw new InvalidOperationException("Expected a managed session.");
                return opened;
            });
            await using ServiceProvider provider = services.BuildServiceProvider();
            IGmsClientFactory factory = provider.GetRequiredService<IGmsClientFactory>();
            await using GmsClient client = await factory.CreateAsync().ConfigureAwait(false);
            Assert.That((await client.DiscoverAsync().ConfigureAwait(false)).Count, Is.EqualTo(1));
            await client.DisposeAsync().ConfigureAwait(false);
            Assert.That(opened!.Connected, Is.False);
        }

        /// <summary>
        /// Cancellation after connection establishment releases the newly acquired session before returning failure.
        /// </summary>
        [Test]
        public async Task ClientFactoryCancellationAfterOpenClosesAcquiredSessionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            ManagedSession? opened = null;
            var factory = new GmsClientFactory(async _ =>
            {
                ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
                opened = session as ManagedSession ?? throw new InvalidOperationException("Expected managed session.");
                cancellation.Cancel();
                return opened;
            }, NUnitTelemetryContext.Create());
            Assert.That(() => factory.CreateAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(opened, Is.Not.Null);
            Assert.That(opened!.Connected, Is.False);
        }
    }
}
