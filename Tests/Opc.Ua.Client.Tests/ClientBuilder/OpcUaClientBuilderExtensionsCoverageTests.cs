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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Patch-coverage tests for <see cref="OpcUaClientBuilderExtensions"/>.
    /// Covers null guards, newly registered DI services, and the conditional
    /// branches in <c>ApplyManagedSessionOptions</c> that are exercised via
    /// <see cref="IManagedSessionFactory.ConnectAsync"/> with a pre-cancelled
    /// token to avoid live network calls.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaClientBuilderExtensionsCoverageTests
    {
        [Test]
        public void AddDiscoveryOnClientBuilderNullBuilderThrows()
        {
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddDiscovery(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddSubscriptionsNullBuilderThrows()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddSubscriptions(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddManagedClientPoolNullBuilderThrows()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddManagedClientPool(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddReverseConnectClientNullBuilderThrows()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddReverseConnectClient(
                    null!,
                    _ => { },
                    new Uri("urn:test")),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddReverseConnectClientNullConfigureThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddReverseConnectClient(null!, new Uri("urn:test")),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddReverseConnectClientNullUriThrows()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddReverseConnectClient(_ => { }, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDiscoveryAndConnectNullBuilderThrows()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddDiscoveryAndConnect(null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDiscoveryAndConnectNullConfigureThrows()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            Assert.That(
                () => builder.AddDiscoveryAndConnect(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task SelectDiscoveredEndpointNoMatchThrowsAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(opts => opts.Configuration = CreateConfig())
                .AddDiscoveryAndConnect(opts =>
                {
                    opts.DiscoveryUrl = "opc.tcp://test:4840";
                    opts.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    opts.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                });

            // Override discovery service: returns endpoints that do NOT match configured criteria.
            services.AddSingleton<IOpcUaDiscoveryService>(new EmptyDiscoveryStub());

            using ServiceProvider sp = services.BuildServiceProvider();
            var factory =
                sp.GetRequiredService<Func<CancellationToken, Task<Client.ManagedSession>>>();

            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => factory(CancellationToken.None));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("No discovered endpoint matched"));

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public void RegisterCoreServicesRegistersManagedSessionFactoryAndConnector()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opts => opts.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IManagedSessionFactory>(),
                Is.InstanceOf<DefaultManagedSessionFactory>());
            Assert.That(sp.GetService<IManagedSessionConnector>(), Is.Not.Null);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsPreferredLocalesAsync()
        {
            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services => services.AddOpcUa().AddClient(opts =>
                {
                    opts.Configuration = CreateConfig();
                    opts.Session = new ManagedSessionOptions
                    {
                        PreferredLocales = new List<string> { "en-US", "de-DE" }
                    };
                })).ConfigureAwait(false);

            Assert.That(captured.PreferredLocales, Has.Count.EqualTo(2));
            Assert.That(captured.PreferredLocales[0], Is.EqualTo("en-US"));
            Assert.That(captured.PreferredLocales[1], Is.EqualTo("de-DE"));
        }

        [Test]
        public async Task ApplyManagedSessionOptionsSubscriptionEngineFactoryAsync()
        {
            ISubscriptionEngineFactory engineFactory = new DefaultSubscriptionEngineFactory();

            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services => services.AddOpcUa().AddClient(opts =>
                {
                    opts.Configuration = CreateConfig();
                    opts.Session = new ManagedSessionOptions
                    {
                        SubscriptionEngineFactory = engineFactory
                    };
                })).ConfigureAwait(false);

            Assert.That(captured.SubscriptionEngineFactory, Is.SameAs(engineFactory));
        }

        [Test]
        public async Task ApplyManagedSessionOptionsFlagsAsync()
        {
            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services => services.AddOpcUa().AddClient(opts =>
                {
                    opts.Configuration = CreateConfig();
                    opts.Session = new ManagedSessionOptions
                    {
                        EnableServerRedundancy = true,
                        EnableTokenReuseFailover = true,
                        TransferSubscriptionsOnRecreate = false,
                        PoolNotifications = true,
                        ModelChangeTracking = true
                    };
                })).ConfigureAwait(false);

            Assert.That(captured.EnableServerRedundancy, Is.True);
            Assert.That(captured.EnableTokenReuseFailover, Is.True);
            Assert.That(captured.TransferSubscriptionsOnRecreate, Is.False);
            Assert.That(captured.PoolNotifications, Is.True);
            Assert.That(captured.ModelChangeTracking, Is.True);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsTimeProviderAsync()
        {
            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services => services.AddOpcUa().AddClient(opts =>
                {
                    opts.Configuration = CreateConfig();
                    opts.Session = new ManagedSessionOptions
                    {
                        TimeProvider = TimeProvider.System
                    };
                })).ConfigureAwait(false);

            Assert.That(captured.TimeProvider, Is.SameAs(TimeProvider.System));
        }

        [Test]
        public async Task ApplyManagedSessionOptionsWithRegisteredIdentityProviderAsync()
        {
            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services => services.AddOpcUa()
                    .AddClient(opts => opts.Configuration = CreateConfig())
                    .AddIdentityProvider<AnonymousIdentityProvider>()).ConfigureAwait(false);

            Assert.That(captured.IdentityProvider, Is.InstanceOf<AnonymousIdentityProvider>());
        }

        [Test]
        public async Task ApplyManagedSessionOptionsWithMultipleIdentityProvidersAsync()
        {
            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services =>
            {
                services.AddOpcUa()
                    .AddClient(opts => opts.Configuration = CreateConfig());

                services.AddSingleton<IClientIdentityProvider, AnonymousIdentityProvider>();
                services.AddSingleton<IClientIdentityProvider, AnonymousIdentityProvider>();
            }).ConfigureAwait(false);

            Assert.That(captured.IdentityProvider, Is.InstanceOf<CompositeClientIdentityProvider>());
        }

        [Test]
        public async Task ApplyManagedSessionOptionsWithIdentityProviderInSessionOptionsAsync()
        {
            IClientIdentityProvider provider = new AnonymousIdentityProvider();
            ManagedSessionOptions captured = await CaptureAppliedSessionOptionsAsync(services => services.AddOpcUa().AddClient(opts =>
                {
                    opts.Configuration = CreateConfig();
                    opts.Session = new ManagedSessionOptions
                    {
                        IdentityProvider = provider
                    };
                })).ConfigureAwait(false);

            Assert.That(captured.IdentityProvider, Is.SameAs(provider));
        }

        private static Task<ManagedSessionOptions> CaptureAppliedSessionOptionsAsync(
            Action<ServiceCollection> configureServices)
        {
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IClientChannelManager>());
            configureServices(services);

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            ManagedSessionOptions? captured = null;

            OperationCanceledException? ex = Assert.CatchAsync<OperationCanceledException>(async () =>
                await factory.ConnectAsync(
                    CreateEndpoint(),
                    builder =>
                    {
                        captured = builder.Build();
                        throw new OperationCanceledException();
                    },
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(captured, Is.Not.Null);
            return Task.FromResult(captured!);
        }

        private static ApplicationConfiguration CreateConfig()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client-cov",
                ApplicationName = "client-cov",
                ClientConfiguration = new ClientConfiguration()
            };
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://127.0.0.1:62000"
            }, null);
        }

        /// <summary>
        /// Minimal discovery stub that returns an empty endpoint list so that
        /// <c>SelectDiscoveredEndpointAsync</c> always reaches the "no match" throw.
        /// </summary>
        private sealed class EmptyDiscoveryStub : IOpcUaDiscoveryService
        {
            public ValueTask<ArrayOf<ApplicationDescription>> FindServersAsync(
                string discoveryUrl,
                ArrayOf<string> serverUris = default,
                CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<ApplicationDescription>>(
                    []);
            }

            public ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
                string discoveryUrl,
                ArrayOf<string> profileUris = default,
                CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<EndpointDescription>>(
                    []);
            }
        }
    }
}
