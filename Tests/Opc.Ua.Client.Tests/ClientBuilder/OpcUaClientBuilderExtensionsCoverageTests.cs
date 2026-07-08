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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
                () => OpcUaClientBuilderExtensions.AddDiscovery((IOpcUaClientBuilder)null!),
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
                    (IOpcUaBuilder)null!,
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
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opts =>
            {
                opts.Configuration = CreateConfig();
                opts.Session = new ManagedSessionOptions
                {
                    PreferredLocales = new List<string> { "en-US", "de-DE" }
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsSubscriptionEngineFactoryAsync()
        {
            ISubscriptionEngineFactory engineFactory = new DefaultSubscriptionEngineFactory();

            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opts =>
            {
                opts.Configuration = CreateConfig();
                opts.Session = new ManagedSessionOptions
                {
                    SubscriptionEngineFactory = engineFactory
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsFlagsAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opts =>
            {
                opts.Configuration = CreateConfig();
                opts.Session = new ManagedSessionOptions
                {
                    EnableServerRedundancy = true,
                    TransferSubscriptionsOnRecreate = true,
                    PoolNotifications = true,
                    ModelChangeTracking = true
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsTimeProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opts =>
            {
                opts.Configuration = CreateConfig();
                opts.Session = new ManagedSessionOptions
                {
                    TimeProvider = TimeProvider.System
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsWithRegisteredIdentityProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(opts => opts.Configuration = CreateConfig())
                .AddIdentityProvider<AnonymousIdentityProvider>();

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsWithMultipleIdentityProvidersAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(opts => opts.Configuration = CreateConfig());

            // Register two providers so ResolveIdentityProvider returns a CompositeClientIdentityProvider.
            services.AddSingleton<IClientIdentityProvider, AnonymousIdentityProvider>();
            services.AddSingleton<IClientIdentityProvider, AnonymousIdentityProvider>();

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyManagedSessionOptionsWithIdentityProviderInSessionOptionsAsync()
        {
            IClientIdentityProvider provider = new AnonymousIdentityProvider();
            _ = new AnonymousIdentityProvider();

            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opts =>
            {
                opts.Configuration = CreateConfig();
                opts.Session = new ManagedSessionOptions
                {
                    IdentityProvider = provider
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory.ConnectAsync(CreateEndpoint(), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(ex, Is.Not.Null);
            Assert.That(ex, Is.Not.TypeOf<Microsoft.Extensions.Options.OptionsValidationException>());

            await Task.CompletedTask.ConfigureAwait(false);
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
                    ArrayOf<ApplicationDescription>.Empty);
            }

            public ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
                string discoveryUrl,
                ArrayOf<string> profileUris = default,
                CancellationToken ct = default)
            {
                return new ValueTask<ArrayOf<EndpointDescription>>(
                    ArrayOf<EndpointDescription>.Empty);
            }
        }
    }
}
