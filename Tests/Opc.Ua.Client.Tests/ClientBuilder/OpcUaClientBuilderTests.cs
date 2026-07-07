// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    [TestFixture]
    [Category("Client")]
    [Category("ClientBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaClientBuilderTests
    {
        [Test]
        public void AddClientThrowsForNullArgs()
        {
            Assert.That(
                () => OpcUaClientBuilderExtensions.AddClient(null!, _ => { }),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddClient((Action<OpcUaClientOptions>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddClientRegistersExpectedServices()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.Configuration = CreateConfig();
                opt.Session = new ManagedSessionOptions
                {
                    Endpoint = new ConfiguredEndpoint(null, new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840"
                    }, configuration: null)
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<OpcUaClientOptions>(), Is.Not.Null);
            Assert.That(sp.GetService<ITelemetryContext>(), Is.Not.Null);
            Assert.That(sp.GetService<ISessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<ManagedSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<Func<CancellationToken, Task<Client.ManagedSession>>>(),
                Is.Not.Null);
        }

        [Test]
        public void AddClientReturnsBuilderWithServices()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddClient(opt => opt.Configuration = CreateConfig());

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));
        }

        [Test]
        public void SessionFactoryHasV2EngineByDefault()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt => opt.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            ISessionFactory? factory = sp.GetService<ISessionFactory>();
            Assert.That(factory, Is.Not.Null);
            Assert.That(factory, Is.InstanceOf<DefaultSessionFactory>());
            var dsf = (DefaultSessionFactory)factory!;
            Assert.That(dsf.SubscriptionEngineFactory, Is.Not.Null);
            Assert.That(dsf.SubscriptionEngineFactory,
                Is.InstanceOf<DefaultSubscriptionEngineFactory>());
        }


        [Test]
        public void AddSubscriptionsAndPoolRegisterServices()
        {
            var services = new ServiceCollection();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddClient(opt => opt.Configuration = CreateConfig())
                .AddSubscriptions()
                .AddManagedClientPool();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(sp.GetService<IManagedSessionPool>(), Is.Not.Null);
            Assert.That(sp.GetRequiredService<IOptionsMonitor<Subscriptions.SubscriptionOptions>>()
                .CurrentValue, Is.Not.Null);
            Assert.That(sp.GetService<IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions>>(), Is.Not.Null);
        }

        [Test]
        public void AddDiscoveryAndConnectRegistersDiscoveryFactory()
        {
            var services = new ServiceCollection();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddClient(opt => opt.Configuration = CreateConfig())
                .AddDiscoveryAndConnect(options =>
                {
                    options.DiscoveryUrl = "opc.tcp://localhost:4840";
                    options.SecurityMode = MessageSecurityMode.None;
                    options.SecurityPolicyUri = SecurityPolicies.None;
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(sp.GetService<Discovery.IOpcUaDiscoveryService>(), Is.Not.Null);
            Assert.That(sp.GetService<DiscoveryConnectOptions>(), Is.Not.Null);
            Assert.That(sp.GetService<Func<CancellationToken, Task<Client.ManagedSession>>>(), Is.Not.Null);
        }

        [Test]
        public void AddReverseConnectClientRegistersReverseFactory()
        {
            var services = new ServiceCollection();
            ConfiguredEndpoint endpoint = CreateEndpoint();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddReverseConnectClient(options =>
                {
                    options.Configuration = CreateConfig();
                    options.Session = options.Session with { Endpoint = endpoint };
                }, new Uri("urn:test:server"));

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(sp.GetService<ReverseConnectManager>(), Is.Not.Null);
            Assert.That(sp.GetService<Func<CancellationToken, Task<Client.ManagedSession>>>(), Is.Not.Null);
        }

        [Test]
        public async Task ManagedSessionPoolDisposeDisposesCachedSessionsAsync()
        {
            Client.ManagedSession session = CreateUnconnectedManagedSession();
            var pool = new ManagedSessionPool(new FixedManagedSessionFactory(session));

            await pool.GetOrConnectAsync("primary", CreateEndpoint()).ConfigureAwait(false);
            pool.Dispose();

            Assert.That(GetDisposedFlag(session), Is.EqualTo(1));
        }

        [Test]
        public void ManagedSessionPoolEvictsFailedConnectTasks()
        {
            Client.ManagedSession session = CreateUnconnectedManagedSession();
            var factory = new FailsOnceManagedSessionFactory(session);
            var pool = new ManagedSessionPool(factory);

            Assert.That(
                () => pool.GetOrConnectAsync("primary", CreateEndpoint()),
                Throws.InvalidOperationException);
            Assert.That(
                () => pool.GetOrConnectAsync("primary", CreateEndpoint()),
                Throws.Nothing);
            Assert.That(factory.Attempts, Is.EqualTo(2));
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

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            }, configuration: null);
        }

        private static Client.ManagedSession CreateUnconnectedManagedSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ConstructorInfo constructor = typeof(Client.ManagedSession).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [
                    typeof(ApplicationConfiguration),
                    typeof(ConfiguredEndpoint),
                    typeof(ISessionFactory),
                    typeof(IReconnectPolicy),
                    typeof(IServerRedundancyHandler),
                    typeof(ILogger),
                    typeof(IUserIdentity),
                    typeof(IClientIdentityProvider),
                    typeof(TimeProvider),
                    typeof(ArrayOf<string>),
                    typeof(string),
                    typeof(uint),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(IClientChannelManager),
                    typeof(IClientConnectGate)
                ],
                null)!;

            return (Client.ManagedSession)constructor.Invoke(
            [
                CreateConfig(),
                CreateEndpoint(),
                new DefaultSessionFactory(telemetry),
                new ReconnectPolicy(new ReconnectPolicyOptions()),
                null,
                telemetry.CreateLogger<Client.ManagedSession>(),
                null,
                null,
                TimeProvider.System,
                default(ArrayOf<string>),
                "PooledSession",
                60000u,
                false,
                false,
                false,
                null,
                null
            ]);
        }

        private static int GetDisposedFlag(Client.ManagedSession session)
        {
            FieldInfo field = typeof(Client.ManagedSession).GetField(
                "m_disposed",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (int)field.GetValue(session)!;
        }

        private sealed class FixedManagedSessionFactory : IManagedSessionFactory
        {
            public FixedManagedSessionFactory(Client.ManagedSession session)
            {
                m_session = session;
            }

            public Task<Client.ManagedSession> ConnectAsync(
                ConfiguredEndpoint endpoint,
                CancellationToken ct = default)
            {
                return Task.FromResult(m_session);
            }

            public Task<Client.ManagedSession> ConnectAsync(
                ConfiguredEndpoint endpoint,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct = default)
            {
                return Task.FromResult(m_session);
            }

            public Task<Client.ManagedSession> ConnectReverseAsync(
                ReverseConnectManager manager,
                Uri serverUri,
                ConfiguredEndpoint endpoint,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task<Client.ManagedSession> ConnectReverseAsync(
                ReverseConnectManager manager,
                Uri serverUri,
                ConfiguredEndpoint endpoint,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            private readonly Client.ManagedSession m_session;
        }

        private sealed class FailsOnceManagedSessionFactory : IManagedSessionFactory
        {
            public FailsOnceManagedSessionFactory(Client.ManagedSession session)
            {
                m_session = session;
            }

            public int Attempts { get; private set; }

            public Task<Client.ManagedSession> ConnectAsync(
                ConfiguredEndpoint endpoint,
                CancellationToken ct = default)
            {
                return ConnectAsync(endpoint, _ => { }, ct);
            }

            public Task<Client.ManagedSession> ConnectAsync(
                ConfiguredEndpoint endpoint,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct = default)
            {
                Attempts++;
                if (Attempts == 1)
                {
                    throw new InvalidOperationException("Transient failure.");
                }

                return Task.FromResult(m_session);
            }

            public Task<Client.ManagedSession> ConnectReverseAsync(
                ReverseConnectManager manager,
                Uri serverUri,
                ConfiguredEndpoint endpoint,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public Task<Client.ManagedSession> ConnectReverseAsync(
                ReverseConnectManager manager,
                Uri serverUri,
                ConfiguredEndpoint endpoint,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            private readonly Client.ManagedSession m_session;
        }
    }
}
