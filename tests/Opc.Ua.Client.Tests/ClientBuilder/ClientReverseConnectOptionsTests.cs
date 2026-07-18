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
