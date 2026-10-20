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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Exercises actual hosting and injection, not merely the presence of registration methods.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CraneHostingTests
    {
        /// <summary>
        /// Standalone and both existing-owner registrations run the same runtime and drain it on shutdown.
        /// </summary>
        [TestCase("standalone")]
        [TestCase("machinery")]
        [TestCase("robotics")]
        public async Task HostingUsesOneOwnerAndTheSameRuntime(string owner)
        {
            const string instanceUri = "urn:crane:hosting:instances";
            await using var provider = new SimulatedCraneProvider(CraneRuntimeTestData.Samples("HostedCrane"));
            var services = new ServiceCollection();
            services.AddLogging();
            int port = FreePort();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "CraneHostingTest";
                options.ApplicationUri = "urn:localhost:crane:hosting:test";
                options.PkiRoot = CraneTestPaths.NewPkiPath();
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeUnsecurePolicyNone = true;
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(
                    "opc.tcp://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/Crane");
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
            });
            CraneSystemHandle? built = null;
            IServerInternal? runningServer = null;
            var ready = new TaskCompletionSource<CraneSystemHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (owner == "robotics")
            {
                builder.AddRobotics(options => options.InstanceNamespaceUri = instanceUri)
                    .AddCranesHoistsToRobotics()
                    .ConfigureCranesHoistsFor<RoboticsNodeManager>(async (context, cancellationToken) =>
                    {
                        runningServer = ((ServerSystemContext)context.Robotics.Context).Server;
                        built = await CraneRuntimeTestData.Define(context, "HostedCrane", provider)
                            .BuildAsync(cancellationToken).ConfigureAwait(false);
                        ready.TrySetResult(built);
                    });
            }
            else
            {
                if (owner == "standalone")
                {
                    builder.AddCranesHoists(options => options.InstanceNamespaceUri = instanceUri);
                }
                else
                {
                    builder.AddMachinery(options =>
                    {
                        options.Parts = MachineryParts.BuildingBlocks;
                        options.InstanceNamespaceUri = instanceUri;
                    }).AddCranesHoistsToMachinery();
                }
                builder.ConfigureCranesHoists(async context =>
                {
                    runningServer = ((ServerSystemContext)context.Robotics.Context).Server;
                    built = await CraneRuntimeTestData.Define(context, "HostedCrane", provider).BuildAsync()
                        .ConfigureAwait(false);
                    ready.TrySetResult(built);
                });
            }
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            Assert.That(serviceProvider.GetServices<OpcUaServerNodeManagerRegistration>(), Has.Exactly(1).Items);
            IHostedService[] hosted = serviceProvider.GetServices<IHostedService>().ToArray();
            try
            {
                foreach (IHostedService service in hosted)
                {
                    await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
                }
                built = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (runningServer?.IsRunning != true)
                {
                    await Task.Delay(20, startup.Token).ConfigureAwait(false);
                }
                Assert.That(built, Is.Not.Null, "ConfigureCranesHoists must actually run during startup.");
                Assert.That(built!.State, Is.TypeOf<CraneMotionDeviceSystemState>());
                Assert.That(built.ReadSignal(new("HostedCrane", CraneSignal.SystemOn)).WrappedValue.GetBoolean(),
                    Is.True);
                Assert.That(built.IsDisposed, Is.False);
            }
            finally
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                for (int i = hosted.Length - 1; i >= 0; i--)
                {
                    await hosted[i].StopAsync(deadline.Token).ConfigureAwait(false);
                }
            }
            Assert.That(built!.IsDisposed, Is.True);
        }

        /// <summary>
        /// Cancellation after publishing a crane but before server startup completes must release committed bindings.
        /// </summary>
        [TestCase("standalone")]
        [TestCase("machinery")]
        [TestCase("robotics")]
        public async Task StartupCancellationAfterPublicationDrainsCraneBindings(string owner)
        {
            await using var equipment = new SimulatedCraneProvider(CraneRuntimeTestData.Samples("StartupCrane"));
            var ready = new TaskCompletionSource<CraneSystemHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            services.AddLogging();
            int port = FreePort();
            IOpcUaServerBuilder server = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "CraneStartupCancellation";
                options.ApplicationUri = "urn:localhost:crane:startup-cancellation";
                options.PkiRoot = CraneTestPaths.NewPkiPath();
                options.EndpointUrls.Clear();
                options.EndpointUrls.Add(
                    "opc.tcp://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/Crane");
            });
            async ValueTask ConfigureAsync(CranesHoistsBuildContext context, CancellationToken cancellationToken)
            {
                CraneSystemHandle handle = await CraneRuntimeTestData.Define(context, "StartupCrane", equipment)
                    .WithProvider(equipment, ownsProvider: true).BuildAsync(cancellationToken).ConfigureAwait(false);
                ready.TrySetResult(handle);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            if (owner == "robotics")
            {
                server.AddRobotics().AddCranesHoistsToRobotics()
                    .ConfigureCranesHoistsFor<RoboticsNodeManager>(ConfigureAsync);
            }
            else
            {
                if (owner == "standalone")
                {
                    server.AddCranesHoists();
                }
                else
                {
                    server.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
                        .AddCranesHoistsToMachinery();
                }
                server.ConfigureCranesHoists(ConfigureAsync);
            }
            await using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService[] hosted = provider.GetServices<IHostedService>().ToArray();
            CraneSystemHandle? crane = null;
            try
            {
                foreach (IHostedService service in hosted)
                {
                    await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
                }
                crane = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                for (int i = hosted.Length - 1; i >= 0; i--)
                {
                    await hosted[i].StopAsync(stopping.Token).ConfigureAwait(false);
                }
                Assert.That(crane.IsDisposed, Is.True);
            }
            finally
            {
                using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                for (int i = hosted.Length - 1; i >= 0; i--)
                {
                    await hosted[i].StopAsync(stopping.Token).ConfigureAwait(false);
                }
                if (crane != null)
                {
                    await crane.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Standalone registration cannot silently create a second DI namespace owner.
        /// </summary>
        [Test]
        public void DuplicateNamespaceOwnershipIsRejected()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { }).AddCranesHoists();
            Assert.Throws<InvalidOperationException>(() => builder.AddRobotics());
        }

        /// <summary>
        /// Client DI registration is idempotent and exposes the existing managed-session factory shape.
        /// </summary>
        [Test]
        public async Task ClientRegistrationProvidesOneTypedFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddClient("opc.tcp://localhost:4840/NotConnected")
                .AddCranesHoistsClient().AddCranesHoistsClient();
            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetServices<CranesHoistsClientFactory>(), Has.Exactly(1).Items);
            Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<CranesHoistsClient>>>(), Is.Not.Null);
        }

        /// <summary>
        /// Factory validation and acquisition failure do not manufacture a connected client.
        /// </summary>
        [Test]
        public async Task FactoryPreservesAcquisitionErrorsAndCancellation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Throws<ArgumentNullException>(() => new CranesHoistsClientFactory(null!, telemetry));
            Assert.Throws<ArgumentNullException>(() => new CranesHoistsClientFactory(
                _ => Task.FromResult<ManagedSession>(null!), null!));
            var failure = new InvalidOperationException("No session available.");
            var factory = new CranesHoistsClientFactory(_ => Task.FromException<ManagedSession>(failure), telemetry);
            Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory.CreateAsync().ConfigureAwait(false)), Is.SameAs(failure));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await factory.CreateAsync(canceled.Token).ConfigureAwait(false));
        }

        /// <summary>
        /// Allocates a loopback-only ephemeral test endpoint.
        /// </summary>
        private static int FreePort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }
}
