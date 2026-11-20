/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The transport listeners are bound while the server starts but take no
    /// connection before the application is started. A client that connects
    /// during startup is served once the server is running instead of getting
    /// BadServerHalted.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ListenerStartupOrderTests
    {
        [Test]
        public async Task ClientConnectingDuringStartupIsServedAfterStartAsync()
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "ua-lso-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<StartupGateServer>(t => new StartupGateServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            int port = ServerFixtureUtils.GetNextFreeIPPort();
            Task<StartupGateServer>? start = null;
            try
            {
                start = Task.Run(() => fixture.StartAsync(pkiRoot, port));

                // Wait until the server is inside StartApplicationAsync. An
                // attempt that fails on a taken port fails before it gets
                // there, so the fixture's current base address is the port
                // that is bound.
                Task entered = await Task.WhenAny(StartupGateServer.Entered.Task, start)
                    .WaitAsync(TimeSpan.FromSeconds(60))
                    .ConfigureAwait(false);
                Assert.That(entered, Is.SameAs(StartupGateServer.Entered.Task),
                    "The server must reach StartApplicationAsync.");

                int boundPort = new Uri(fixture.Config.ServerConfiguration!.BaseAddresses[0]).Port;
                Task<ArrayOf<EndpointDescription>> early = GetEndpointsAsync(boundPort);

                // Before the fix the listener accepts here and the request
                // completes with BadServerHalted (or a startup race).
                Task first = await Task.WhenAny(early, Task.Delay(TimeSpan.FromSeconds(2)))
                    .ConfigureAwait(false);
                Assert.That(first, Is.Not.SameAs(early),
                    "No request may be served before the server has started: " +
                    (early.IsFaulted ? early.Exception!.InnerException!.Message : "completed"));

                StartupGateServer.Release.TrySetResult(true);
                await start.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

                ArrayOf<EndpointDescription> endpoints = await early
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                Assert.That(endpoints.Count, Is.GreaterThan(0));
            }
            finally
            {
                StartupGateServer.Release.TrySetResult(true);
                if (start != null)
                {
                    try
                    {
                        await start.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // reported by the assertions above
                    }
                }
                await fixture.StopAsync().ConfigureAwait(false);
                StartupGateServer.Reset();
                if (Directory.Exists(pkiRoot))
                {
                    try
                    {
                        Directory.Delete(pkiRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                        // best effort
                    }
                }
            }
        }

        /// <summary>
        /// A TCP listener that was opened deferred and then activated keeps
        /// accepting after a stop/start cycle of the listener.
        /// </summary>
        [Test]
        public async Task ActivatedTcpListenerAcceptsAfterRestartAsync()
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "ua-lsr-" + Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<StartupGateServer>(t => new StartupGateServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            StartupGateServer.Release.TrySetResult(true);
            try
            {
                StartupGateServer server = await fixture.StartAsync(pkiRoot).ConfigureAwait(false);
                TcpTransportListener listener = server.Listeners.OfType<TcpTransportListener>().Single();
                Assert.That(listener.IsAccepting, Is.True);

                listener.Stop();
                listener.Start();

                Assert.That(listener.IsAccepting, Is.True);
                ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync(fixture.Port)
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                Assert.That(endpoints.Count, Is.GreaterThan(0));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                StartupGateServer.Reset();
                if (Directory.Exists(pkiRoot))
                {
                    try
                    {
                        Directory.Delete(pkiRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                        // best effort
                    }
                }
            }
        }

        /// <summary>
        /// The caller that starts the listener activation can cancel its own
        /// wait while a listener is still activating; the activation goes on
        /// and a later caller observes its completion.
        /// </summary>
        [Test]
        public async Task StartingCallerCanCancelPendingActivationAsync()
        {
            var activating = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = new Mock<ITransportListener>();
            listener.Setup(l => l.ListenerId).Returns("pending");
            listener.As<ITransportListenerAcceptControl>()
                .Setup(l => l.StartAcceptingAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(activating.Task));

            using var server = new StartupGateServer(NUnitTelemetryContext.Create());
            server.AddListener(listener.Object);

            using var cts = new CancellationTokenSource();
            Task first = server.StartAcceptingConnectionsAsync(cts.Token).AsTask();
            Assert.That(first.IsCompleted, Is.False);

            cts.Cancel();
            Assert.That(
                async () => await first.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(server.IsAcceptingConnections, Is.False);

            activating.TrySetResult(true);
            await server.StartAcceptingConnectionsAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(server.IsAcceptingConnections, Is.True);
            listener.As<ITransportListenerAcceptControl>().Verify(
                l => l.StartAcceptingAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }

        /// <summary>
        /// The hosted server serves clients only after its startup tasks ran,
        /// unless <see cref="OpcUaServerOptions.ServeClientsDuringStartupTasks"/>
        /// is set.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostedServerServesClientsAfterStartupTasksAsync(bool serveDuringStartupTasks)
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "ua-lsoh-" + Guid.NewGuid().ToString("N"));
            int port = ServerFixtureUtils.GetNextFreeIPPort();
            var gate = new BlockingStartupTask();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(o =>
                {
                    o.ApplicationName = "ListenerStartupOrderTest";
                    o.ApplicationUri = "urn:localhost:ListenerStartupOrderTest";
                    o.ProductUri = "urn:localhost:ListenerStartupOrderTest:product";
                    o.PkiRoot = Path.Combine(testRoot, "pki");
                    o.AutoAcceptUntrustedCertificates = true;
                    o.IncludeUnsecurePolicyNone = true;
                    o.ServeClientsDuringStartupTasks = serveDuringStartupTasks;
                    o.EndpointUrls.Clear();
                    o.EndpointUrls.Add(
                        "opc.tcp://localhost:" +
                        port.ToString(CultureInfo.InvariantCulture) +
                        "/ListenerStartupOrderTest");
                });
            services.AddSingleton<IServerStartupTask>(gate);

            using ServiceProvider provider = services.BuildServiceProvider();
            IHostedService hostedService = provider.GetServices<IHostedService>().Single();
            try
            {
                await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Task execute = ((BackgroundService)hostedService).ExecuteTask!;
                Task entered = await Task.WhenAny(gate.Entered.Task, execute)
                    .WaitAsync(TimeSpan.FromSeconds(60))
                    .ConfigureAwait(false);
                if (entered != gate.Entered.Task)
                {
                    await execute.ConfigureAwait(false);
                }
                Assert.That(entered, Is.SameAs(gate.Entered.Task),
                    "The hosted server must run its startup task.");

                Task<ArrayOf<EndpointDescription>> early = GetEndpointsAsync(port);
                Task first = await Task.WhenAny(early, Task.Delay(TimeSpan.FromSeconds(2)))
                    .ConfigureAwait(false);
                if (serveDuringStartupTasks)
                {
                    Assert.That(first, Is.SameAs(early),
                        "The opt-out serves clients while the startup tasks run.");
                }
                else
                {
                    Assert.That(first, Is.Not.SameAs(early),
                        "No request may be served before the startup tasks ran: " +
                        (early.IsFaulted ? early.Exception!.InnerException!.Message : "completed"));
                }

                gate.Release.TrySetResult(true);
                ArrayOf<EndpointDescription> endpoints = await early
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
                Assert.That(endpoints.Count, Is.GreaterThan(0));
            }
            finally
            {
                gate.Release.TrySetResult(true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await hostedService.StopAsync(cts.Token).ConfigureAwait(false);
                if (Directory.Exists(testRoot))
                {
                    try
                    {
                        Directory.Delete(testRoot, recursive: true);
                    }
                    catch (IOException)
                    {
                        // best effort
                    }
                }
            }
        }

        internal static async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync(int port)
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 30000;
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                new Uri($"opc.tcp://localhost:{port}"),
                endpointConfiguration,
                NUnitTelemetryContext.Create()).ConfigureAwait(false);
            return await client.GetEndpointsAsync(default, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A startup task that holds the hosted start until the test releases it.
    /// </summary>
    internal sealed class BlockingStartupTask : IServerStartupTask
    {
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Holds StartApplicationAsync until the test releases it.
    /// </summary>
    internal sealed class StartupGateServer : ReferenceServer
    {
        public StartupGateServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        public static TaskCompletionSource<bool> Entered { get; private set; } = NewSignal();

        public ITransportListener[] Listeners => [.. TransportListeners];

        public void AddListener(ITransportListener listener)
        {
            TransportListeners.Add(listener);
        }

        public static TaskCompletionSource<bool> Release { get; private set; } = NewSignal();

        public static void Reset()
        {
            Entered = NewSignal();
            Release = NewSignal();
        }

        protected override async ValueTask StartApplicationAsync(
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.ConfigureAwait(false);
            await base.StartApplicationAsync(configuration, cancellationToken).ConfigureAwait(false);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
