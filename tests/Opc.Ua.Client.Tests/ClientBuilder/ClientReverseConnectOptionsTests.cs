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

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Coverage tests for the
    /// <see cref="ClientReverseConnectOptions"/> registration path on
    /// <see cref="OpcUaClientOptions.ReverseConnect"/>: non-blocking
    /// resolution, hosted-service registration, eager and lazy startup, and
    /// async disposal.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [Category("ReverseConnect")]
    [Parallelizable]
    public sealed class ClientReverseConnectOptionsTests
    {
        [Test]
        public void ReverseConnectOptionDefaultsToNull()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt => opt.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            OpcUaClientOptions opts = sp.GetRequiredService<OpcUaClientOptions>();
            Assert.That(opts.ReverseConnect, Is.Null);
        }

        [Test]
        public void ReverseConnectManagerIsResolvableWithoutOptions()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt => opt.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();
            Assert.That(manager, Is.Not.Null);
        }

        [Test]
        public void ReverseConnectManagerResolvesWithoutBlockingStart()
        {
            using ServiceProvider sp = BuildProvider();

            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();

            // Resolution only configures startup; it must not open listeners.
            // A not-started manager still accepts endpoint mutation.
            Assert.That(
                () => manager.AddEndpoint(NextFreeListenerUri()),
                Throws.Nothing);
        }

        [Test]
        public void HostedServiceRegisteredOnce()
        {
            using ServiceProvider sp = BuildProvider();

            int count = sp.GetServices<IHostedService>()
                .Count(s => s is ReverseConnectManagerHostedService);

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public async Task EagerHostedStartOpensListeners()
        {
            var harness = new FakeTransportHarness();
            Uri configuredUrl = FakeUri();
            await using ServiceProvider sp =
                BuildFakeTransportProvider(harness, configuredUrl);
            IHostedService hosted = sp.GetServices<IHostedService>()
                .First(s => s is ReverseConnectManagerHostedService);

            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);

            // The exact configured endpoint's listener is actually opened.
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == configuredUrl && l.IsOpen),
                Is.True);

            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);

            // Hosted stop closes the configured listener (real cleanup, not
            // merely a state transition).
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == configuredUrl && l.IsOpen),
                Is.False);
            Assert.That(
                harness.Listeners.Any(
                    l => l.OpenedUrl == configuredUrl && l.CloseCount >= 1),
                Is.True);
        }

        [Test]
        public async Task LazyWaitForConnectionStartsManager()
        {
            var harness = new FakeTransportHarness();
            Uri configuredUrl = FakeUri();
            await using ServiceProvider sp =
                BuildFakeTransportProvider(harness, configuredUrl);
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();

            using var cts = new CancellationTokenSource(millisecondsDelay: 250);
            try
            {
                await manager.WaitForConnectionAsync(
                    FakeUri(),
                    null,
                    cts.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // BadTimeout expected — the wait started the manager first.
            }
            catch (OperationCanceledException)
            {
                // Also acceptable.
            }

            // WaitForConnectionAsync triggered the lazy start which opened the
            // exact configured listener.
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == configuredUrl && l.IsOpen),
                Is.True);
        }

        [Test]
        public void ReverseConnectOptionsMirrorIntoApplicationConfiguration()
        {
            ApplicationConfiguration config = CreateConfig();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = config;
                opt.ReverseConnect = new ClientReverseConnectOptions
                {
                    HoldTimeMs = 25000,
                    WaitTimeoutMs = 30000
                };
                opt.ReverseConnect.ClientEndpointUrls.Add("opc.tcp://localhost:14841");
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();
            Assert.That(manager, Is.Not.Null);

            Assert.That(config.ClientConfiguration, Is.Not.Null);
            Assert.That(config.ClientConfiguration!.ReverseConnect, Is.Not.Null);
            Assert.That(config.ClientConfiguration.ReverseConnect!.HoldTime, Is.EqualTo(25000));
            Assert.That(
                config.ClientConfiguration.ReverseConnect.WaitTimeout,
                Is.EqualTo(30000));
            ArrayOf<ReverseConnectClientEndpoint> endpoints =
                config.ClientConfiguration.ReverseConnect.ClientEndpoints;
            Assert.That(endpoints.IsNull, Is.False);
            Assert.That(endpoints.Count, Is.EqualTo(1));
            Assert.That(endpoints[0].EndpointUrl, Is.EqualTo("opc.tcp://localhost:14841"));
        }

        [Test]
        public async Task MissingConfigurationSurfacedDuringAsyncStart()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.ReverseConnect = new ClientReverseConnectOptions();
                opt.ReverseConnect.ClientEndpointUrls.Add("opc.tcp://localhost:14842");
            });

            await using ServiceProvider sp = services.BuildServiceProvider();

            // Resolution must succeed (no blocking start, no throw).
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();
            Assert.That(manager, Is.Not.Null);

            // The missing configuration surfaces during the async start.
            Assert.That(
                async () => await manager.EnsureStartedAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task AsyncServiceProviderDisposesManager()
        {
            ServiceProvider sp = BuildProvider();
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            await sp.DisposeAsync().ConfigureAwait(false);

            // After async disposal the manager is disposed.
            Assert.That(
                async () => await manager.EnsureStartedAsync().ConfigureAwait(false),
                Throws.InstanceOf<ObjectDisposedException>());
        }

        [Test]
        public async Task InjectedProviderSuppliesEndpointsWhenOptionListIsEmpty()
        {
            ApplicationConfiguration config = CreateConfig();
            var harness = new FakeTransportHarness();
            Uri providerUrl = FakeUri();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = config;
                // No configured endpoint URLs: the injected provider supplies them.
                opt.ReverseConnect = new ClientReverseConnectOptions();
            });
            services.AddSingleton<IReverseConnectConfigurationProvider>(
                new EndpointSupplyingProvider(providerUrl));
            services.AddSingleton<ITransportBindingRegistry>(harness.Registry);

            await using ServiceProvider sp = services.BuildServiceProvider();
            ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();

            // Startup is configured even though the option list is empty, so the
            // provider-supplied endpoint's listener is opened on eager start.
            await manager.EnsureStartedAsync().ConfigureAwait(false);

            // The exact provider-supplied endpoint listener is actually opened.
            Assert.That(
                harness.Listeners.Any(l => l.OpenedUrl == providerUrl && l.IsOpen),
                Is.True);
        }

        [Test]
        public async Task AsyncApplicationConfigurationProviderStartsReverseConnect()
        {
            ApplicationConfiguration config = CreateConfig();
            var harness = new FakeTransportHarness();
            Uri configuredUrl = FakeUri();
            bool getAsyncCalledBeforeListenerOpen = false;
            bool getAsyncCalled = false;
            var configurationProvider =
                new Mock<IOpcUaApplicationConfigurationProvider>();
            configurationProvider
                .Setup(p => p.Configuration)
                .Returns(config);
            configurationProvider
                .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    getAsyncCalled = true;
                    // No listener has been opened for the configured endpoint
                    // yet: the async provider path must complete before the
                    // reverse-connect listener is started.
                    getAsyncCalledBeforeListenerOpen =
                        !harness.Listeners.Any(l => l.OpenedUrl == configuredUrl);
                })
                .ReturnsAsync(config);
            configurationProvider.As<IDisposable>();
            void ConfigureServices(IServiceCollection services)
            {
                services.AddSingleton<IOpcUaApplicationConfigurationProvider>(
                    configurationProvider.Object);
                services.AddOpcUa().AddClient(options =>
                {
                    options.ReverseConnect = new ClientReverseConnectOptions();
                    options.ReverseConnect.ClientEndpointUrls.Add(
                        configuredUrl.ToString());
                });
                services.AddSingleton<ITransportBindingRegistry>(harness.Registry);
            }

#if NET8_0_OR_GREATER
            using IHost host = new HostBuilder()
                .ConfigureServices((_, services) => ConfigureServices(services))
                .Build();
            await host.StartAsync().ConfigureAwait(false);
#else
            var services = new ServiceCollection();
            ConfigureServices(services);
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            _ = serviceProvider
                .GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<OpcUaClientOptions>>()
                .Value;
            IHostedService hosted = serviceProvider.GetServices<IHostedService>()
                .First(s => s is ReverseConnectManagerHostedService);
            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
#endif
            try
            {
                configurationProvider.Verify(
                    p => p.GetAsync(It.IsAny<CancellationToken>()),
                    Times.Once);
                Assert.That(getAsyncCalled, Is.True);
                Assert.That(getAsyncCalledBeforeListenerOpen, Is.True);
                Assert.That(
                    harness.Listeners.Any(
                        l => l.OpenedUrl == configuredUrl && l.IsOpen),
                    Is.True);
            }
            finally
            {
#if NET8_0_OR_GREATER
                await host.StopAsync().ConfigureAwait(false);
#else
                await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
#endif
            }
        }

        [Test]
        public async Task FileBackedRestartPreservesReverseConnectOptionOverlay()
        {
            var harness = new FakeTransportHarness();
            Uri overlayUrl = FakeUri();
            const string updatedName = "UpdatedFromFile";
            string path = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "rcopt_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) +
                    ".cfg");
            System.IO.File.WriteAllText(path, "seed");

            ApplicationConfiguration fileConfig = CreateConfig();
            SetSourceFilePath(fileConfig, path);

            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = fileConfig;
                // In-memory reverse-connect endpoint carried only by the DI
                // option overlay, not by the file.
                opt.ReverseConnect = new ClientReverseConnectOptions();
                opt.ReverseConnect.ClientEndpointUrls.Add(overlayUrl.ToString());
            });
            services.AddSingleton<ITransportBindingRegistry>(harness.Registry);

            await using ServiceProvider sp = services.BuildServiceProvider();
            ReverseConnectManager manager =
                sp.GetRequiredService<ReverseConnectManager>();

            // Simulate the source file changing while stopped: the loader seam
            // returns an updated base configuration WITHOUT reverse-connect
            // endpoints (as a real file load would - the endpoints live only in
            // the DI options overlay).
            manager.ConfigurationFileLoaderForTest = (p, appType, cfgType, ct) =>
            {
                ApplicationConfiguration reloaded = CreateConfig();
                reloaded.ApplicationName = updatedName;
                SetSourceFilePath(reloaded, path);
                return Task.FromResult(reloaded);
            };

            try
            {
                // First start uses the provided file-backed configuration; the
                // overlay endpoint listener opens.
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == overlayUrl && l.IsOpen),
                    Is.True);

                // Stop converts the file-backed seed into a reloading factory.
                await manager.StopServiceAsync().ConfigureAwait(false);
                Assert.That(manager.CurrentStateForTest, Is.EqualTo("Stopped"));

                // Restart reloads the updated file base AND reapplies the DI
                // reverse-connect option overlay, so the overlay endpoint still
                // opens instead of being lost to a plain file load.
                await manager.EnsureStartedAsync().ConfigureAwait(false);

                Assert.That(
                    manager.ActiveApplicationConfigurationForTest?.ApplicationName,
                    Is.EqualTo(updatedName),
                    "the restart must apply the updated file base configuration");
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == overlayUrl && l.IsOpen),
                    Is.True,
                    "the restart must preserve the DI reverse-connect option overlay");
            }
            finally
            {
                await manager.StopServiceAsync().ConfigureAwait(false);
                System.IO.File.Delete(path);
            }
        }

        private static void SetSourceFilePath(ApplicationConfiguration config, string path)
        {
            typeof(ApplicationConfiguration)
                .GetProperty(nameof(ApplicationConfiguration.SourceFilePath))!
                .SetValue(config, path);
        }

        [Test]
        public async Task ProviderInPlaceMutationsDoNotContaminateOverlayAcrossRestart()
        {
            var harness = new FakeTransportHarness();
            Uri overlayUrl = FakeUri();
            Uri providerExtraUrl = FakeUri();
            const int optionHoldTimeMs = 12345;
            const int optionWaitTimeoutMs = 23456;
            string path = System.IO.Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "rcopt_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) +
                    ".cfg");
            System.IO.File.WriteAllText(path, "seed");

            ApplicationConfiguration fileConfig = CreateConfig();
            SetSourceFilePath(fileConfig, path);

            var provider = new MutatingRecordingProvider(providerExtraUrl);
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = fileConfig;
                opt.ReverseConnect = new ClientReverseConnectOptions
                {
                    HoldTimeMs = optionHoldTimeMs,
                    WaitTimeoutMs = optionWaitTimeoutMs
                };
                opt.ReverseConnect.ClientEndpointUrls.Add(overlayUrl.ToString());
            });
            services.AddSingleton<IReverseConnectConfigurationProvider>(provider);
            services.AddSingleton<ITransportBindingRegistry>(harness.Registry);

            await using ServiceProvider sp = services.BuildServiceProvider();
            ReverseConnectManager manager =
                sp.GetRequiredService<ReverseConnectManager>();

            // The file reload returns a fresh base configuration WITHOUT any
            // reverse-connect section, so the endpoints seen by the provider on
            // a restart come exclusively from the DI option overlay.
            manager.ConfigurationFileLoaderForTest = (p, appType, cfgType, ct) =>
            {
                ApplicationConfiguration reloaded = CreateConfig();
                SetSourceFilePath(reloaded, path);
                return Task.FromResult(reloaded);
            };

            try
            {
                // Start, then stop/restart twice. Each start rebuilds the overlay
                // and re-invokes the provider, which mutates the supplied
                // configuration in place (appends its own endpoint, bumps the
                // hold time).
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                await manager.StopServiceAsync().ConfigureAwait(false);
                await manager.EnsureStartedAsync().ConfigureAwait(false);
                await manager.StopServiceAsync().ConfigureAwait(false);
                await manager.EnsureStartedAsync().ConfigureAwait(false);

                Assert.That(
                    provider.Invocations,
                    Has.Count.GreaterThanOrEqualTo(3),
                    "the provider must be invoked on the initial start and each restart");

                // Every provider invocation must receive a clean overlay rebuilt
                // from the immutable option values: exactly the single option
                // endpoint (never the provider's own accumulated extra endpoint)
                // and the original hold/wait timeouts. A shared, mutated overlay
                // instance would leak the previous invocation's changes here.
                foreach (ProviderObservation observation in provider.Invocations)
                {
                    Assert.That(
                        observation.Endpoints,
                        Is.EquivalentTo(new[] { overlayUrl.ToString() }),
                        "each provider invocation must see only the option " +
                        "endpoints, never a mutation accumulated from a prior run");
                    Assert.That(observation.HoldTime, Is.EqualTo(optionHoldTimeMs));
                    Assert.That(observation.WaitTimeout, Is.EqualTo(optionWaitTimeoutMs));
                }

                // The provider's in-place mutation still applies to the live
                // configuration, so its extra endpoint listener is opened.
                Assert.That(
                    harness.Listeners.Any(l => l.OpenedUrl == providerExtraUrl && l.IsOpen),
                    Is.True,
                    "the provider mutation must still take effect for the live run");
            }
            finally
            {
                await manager.StopServiceAsync().ConfigureAwait(false);
                System.IO.File.Delete(path);
            }
        }

        private sealed class ProviderObservation
        {
            public ProviderObservation(string?[] endpoints, int holdTime, int waitTimeout)
            {
                Endpoints = endpoints;
                HoldTime = holdTime;
                WaitTimeout = waitTimeout;
            }

            public string?[] Endpoints { get; }

            public int HoldTime { get; }

            public int WaitTimeout { get; }
        }

        private sealed class MutatingRecordingProvider : IReverseConnectConfigurationProvider
        {
            private readonly Uri m_extraUrl;

            public MutatingRecordingProvider(Uri extraUrl)
            {
                m_extraUrl = extraUrl;
            }

            public List<ProviderObservation> Invocations { get; } = [];

            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var endpoints = new List<string?>();
                if (!configuration.ClientEndpoints.IsNull)
                {
                    foreach (ReverseConnectClientEndpoint endpoint in
                        configuration.ClientEndpoints)
                    {
                        endpoints.Add(endpoint.EndpointUrl);
                    }
                }
                Invocations.Add(new ProviderObservation(
                    endpoints.ToArray(),
                    configuration.HoldTime,
                    configuration.WaitTimeout));

                // Mutate the supplied configuration in place. A single shared
                // overlay instance would carry these mutations into the next
                // reload/restart, contaminating a later provider invocation.
                var mutated = new ReverseConnectClientEndpoint[endpoints.Count + 1];
                for (int i = 0; i < endpoints.Count; i++)
                {
                    mutated[i] = new ReverseConnectClientEndpoint
                    {
                        EndpointUrl = endpoints[i]
                    };
                }
                mutated[endpoints.Count] = new ReverseConnectClientEndpoint
                {
                    EndpointUrl = m_extraUrl.ToString()
                };
                configuration.ClientEndpoints =
                    new ArrayOf<ReverseConnectClientEndpoint>(mutated);
                configuration.HoldTime += 1000;
                return new ValueTask<ReverseConnectClientConfiguration>(configuration);
            }
        }

        private sealed class EndpointSupplyingProvider : IReverseConnectConfigurationProvider
        {
            private readonly Uri m_url;

            public EndpointSupplyingProvider(Uri url)
            {
                m_url = url;
            }

            public ValueTask<ReverseConnectClientConfiguration> ConfigureAsync(
                ApplicationConfiguration? applicationConfiguration,
                ReverseConnectClientConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                configuration.ClientEndpoints = new ArrayOf<ReverseConnectClientEndpoint>(
                    new[]
                    {
                        new ReverseConnectClientEndpoint { EndpointUrl = m_url.ToString() }
                    });
                return new ValueTask<ReverseConnectClientConfiguration>(configuration);
            }
        }

        private static ServiceProvider BuildProvider()
        {
            ApplicationConfiguration config = CreateConfig();
            string url = NextFreeListenerUri().ToString();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = config;
                opt.ReverseConnect = new ClientReverseConnectOptions();
                opt.ReverseConnect.ClientEndpointUrls.Add(url);
            });
            return services.BuildServiceProvider();
        }

        private static ServiceProvider BuildFakeTransportProvider(
            FakeTransportHarness harness,
            params Uri[] urls)
        {
            ApplicationConfiguration config = CreateConfig();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = config;
                opt.ReverseConnect = new ClientReverseConnectOptions();
                foreach (Uri url in urls)
                {
                    opt.ReverseConnect.ClientEndpointUrls.Add(url.ToString());
                }
            });
            // Registered after AddClient so it wins over the default
            // ITransportBindingRegistry for the manager's listener creation.
            services.AddSingleton<ITransportBindingRegistry>(harness.Registry);
            return services.BuildServiceProvider();
        }

        private static Uri NextFreeListenerUri()
        {
            int port = ServerFixtureUtils.GetNextFreeIPPort();
            return new Uri(
                "opc.tcp://localhost:" +
                port.ToString(CultureInfo.InvariantCulture) +
                "/reverse");
        }

        private static Uri FakeUri()
        {
            int port = ServerFixtureUtils.GetNextFreeIPPort();
            return new Uri(
                FakeScheme + "://localhost:" +
                port.ToString(CultureInfo.InvariantCulture) +
                "/reverse");
        }

        private static ApplicationConfiguration CreateConfig()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "test",
                ClientConfiguration = new ClientConfiguration()
            };
        }

        private const string FakeScheme = "opc.fake";

        /// <summary>
        /// A fake <see cref="ITransportBindingRegistry"/> producing recording
        /// listeners for the <see cref="FakeScheme"/> so DI startup tests can
        /// assert that the exact configured/provider endpoint listener is
        /// actually opened and later closed.
        /// </summary>
        private sealed class FakeTransportHarness
        {
            public FakeTransportHarness()
            {
                var factory = new Mock<ITransportListenerFactory>();
                factory.SetupGet(f => f.UriScheme).Returns(FakeScheme);
                factory
                    .Setup(f => f.Create(It.IsAny<ITelemetryContext>()))
                    .Returns(() =>
                    {
                        var listener = new FakeReverseConnectListener();
                        lock (m_listeners)
                        {
                            m_listeners.Add(listener);
                        }
                        return listener;
                    });
                var registry = new DefaultTransportBindingRegistry();
                registry.RegisterListenerFactory(factory.Object);
                Registry = registry;
            }

            public ITransportBindingRegistry Registry { get; }

            public IReadOnlyList<FakeReverseConnectListener> Listeners
            {
                get
                {
                    lock (m_listeners)
                    {
                        return [.. m_listeners];
                    }
                }
            }

            private readonly List<FakeReverseConnectListener> m_listeners = [];
        }

#pragma warning disable CS0067 // events are subscribed by ReverseConnectHost, never raised here
        private sealed class FakeReverseConnectListener : ITransportListener
        {
            public string ListenerId { get; } =
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

            public string UriScheme => FakeScheme;

            public Uri? OpenedUrl { get; private set; }

            public int OpenCount;

            public int CloseCount;

            private int m_isOpen;

            public bool IsOpen => Volatile.Read(ref m_isOpen) != 0;

            public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

            public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

            public ValueTask OpenAsync(
                Uri baseAddress,
                TransportListenerSettings settings,
                ITransportListenerCallback callback,
                CancellationToken ct = default)
            {
                OpenedUrl = baseAddress;
                Interlocked.Increment(ref OpenCount);
                Volatile.Write(ref m_isOpen, 1);
                return default;
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                Interlocked.Increment(ref CloseCount);
                Volatile.Write(ref m_isOpen, 0);
                return default;
            }

            public void CertificateUpdate(
                ICertificateValidatorEx validator,
                ICertificateRegistry serverCertificates)
            {
            }

            public void CreateReverseConnection(Uri url, int timeout)
            {
            }

            public void UpdateChannelLastActiveTime(string globalChannelId)
            {
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }
        }
#pragma warning restore CS0067
    }
}
