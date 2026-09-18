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

//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.

#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Configuration;
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
        public async Task CachedSessionConnectSurvivesCancelledWaitersAndRetriesAfterFailureAsync()
        {
            var pending = new TaskCompletionSource<ApplicationConfiguration>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var configurationProvider = new Mock<IOpcUaApplicationConfigurationProvider>();
            configurationProvider.Setup(provider => provider.Configuration).Returns(CreateConfig());
            int calls = 0;
            configurationProvider.Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    Assert.That(token.CanBeCanceled, Is.False);
                    return Interlocked.Increment(ref calls) == 1
                        ? pending.Task
                        : Task.FromException<ApplicationConfiguration>(new IOException("Retry configuration failure."));
                });
            var services = new ServiceCollection();
            services.AddSingleton(configurationProvider.Object);
            services.AddOpcUa().AddClient(options =>
                options.Session = new ManagedSessionOptions { Endpoint = CreateEndpoint() });
            await using ServiceProvider provider = services.BuildServiceProvider();
            var connect = provider.GetRequiredService<Func<CancellationToken, Task<Client.ManagedSession>>>();
            using var cancellation = new CancellationTokenSource();
            var cancelledWaiters = new Task<Client.ManagedSession>[16];
            for (int ii = 0; ii < cancelledWaiters.Length; ii++)
            {
                cancelledWaiters[ii] = connect(cancellation.Token);
            }
            Task<Client.ManagedSession> survivor = connect(CancellationToken.None);

            cancellation.Cancel();
            await Assert.ThatAsync(
                () => Task.WhenAll(cancelledWaiters).WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(survivor.IsCompleted, Is.False);

            pending.SetException(new IOException("Shared configuration failure."));
            await Assert.ThatAsync(
                () => survivor.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.TypeOf<IOException>().With.Message.EqualTo("Shared configuration failure."))
                .ConfigureAwait(false);
            await Assert.ThatAsync(
                () => connect(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.TypeOf<IOException>().With.Message.EqualTo("Retry configuration failure."))
                .ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public async Task AddClientWithApplicationOptionsBuildsSharedConfigurationAsync()
        {
            string pkiRoot = CreatePkiRoot();
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(opt =>
            {
                opt.ApplicationName = "DirectClient";
                opt.ApplicationUri = "urn:localhost:DirectClient";
                opt.ProductUri = "uri:opcfoundation.org:DirectClient";
                opt.PkiRoot = pkiRoot;
                opt.AutoAcceptUntrustedCertificates = true;
                opt.RejectSHA1SignedCertificates = true;
                opt.MinimumCertificateKeySize = 2048;
            });

            ServiceProvider sp = services.BuildServiceProvider();
            try
            {
                OpcUaClientOptions resolved = sp.GetRequiredService<OpcUaClientOptions>();
                Assert.That(resolved.Configuration, Is.Not.Null);
                Assert.That(resolved.Configuration!.ApplicationName, Is.EqualTo("DirectClient"));
                Assert.That(resolved.Configuration.ClientConfiguration, Is.Not.Null);
                Assert.That(
                    resolved.Configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                    Is.True);

                OpcUaClientOptions optionsValue =
                    sp.GetRequiredService<IOptions<OpcUaClientOptions>>().Value;
                Assert.That(optionsValue.ApplicationName, Is.EqualTo("DirectClient"));
                Assert.That(optionsValue.ApplicationUri, Is.EqualTo("urn:localhost:DirectClient"));
                Assert.That(optionsValue.ProductUri, Is.EqualTo("uri:opcfoundation.org:DirectClient"));
                Assert.That(optionsValue.PkiRoot, Is.EqualTo(pkiRoot));
                Assert.That(optionsValue.AutoAcceptUntrustedCertificates, Is.True);
                Assert.That(optionsValue.RejectSHA1SignedCertificates, Is.True);
                Assert.That(optionsValue.MinimumCertificateKeySize, Is.EqualTo((ushort)2048));
            }
            finally
            {
                await sp.DisposeAsync().ConfigureAwait(false);
                DeletePkiRoot(pkiRoot);
            }
        }

        [Test]
        public void AddClientWithoutConfigurationOrApplicationOptionsStillFailsValidation()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(_ => { });

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options =
                sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            Assert.That(
                () => _ = options.Value,
                Throws.TypeOf<OptionsValidationException>()
                    .With.Property(nameof(OptionsValidationException.Failures))
                    .Some.Contains("OpcUaClientOptions.Configuration is required."));
        }

        [Test]
        public void AddClientThrowsWhenApplicationOptionsCombinedWithExplicitConfiguration()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            Assert.That(
                () => builder.AddClient(opt =>
                {
                    opt.Configuration = CreateConfig();
                    opt.ApplicationName = "Conflicting";
                }),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task AddClientApplicationOptionsComposeWithRootConfigureApplicationRegisteredFirstAsync()
        {
            string pkiRoot = CreatePkiRoot();
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.ConfigureApplication(options => options.ApplicationName = "RootConfigured");
            builder.AddClient(opt =>
            {
                opt.ApplicationName = "ClientOverride";
                opt.PkiRoot = pkiRoot;
                opt.AutoAcceptUntrustedCertificates = true;
            });

            ServiceProvider sp = services.BuildServiceProvider();
            try
            {
                OpcUaClientOptions resolved = sp.GetRequiredService<OpcUaClientOptions>();
                Assert.That(resolved.Configuration, Is.Not.Null);
                // The root ConfigureApplication value was explicitly set, so it wins
                // over the client's application name.
                Assert.That(resolved.Configuration!.ApplicationName, Is.EqualTo("RootConfigured"));
                // A field only set through AddClient still fills the shared configuration.
                Assert.That(
                    resolved.Configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                    Is.True);
            }
            finally
            {
                await sp.DisposeAsync().ConfigureAwait(false);
                DeletePkiRoot(pkiRoot);
            }
        }

        [Test]
        public async Task AddClientApplicationOptionsComposeWithRootConfigureApplicationRegisteredAfterAsync()
        {
            string pkiRoot = CreatePkiRoot();
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddClient(opt =>
            {
                opt.ApplicationName = "ClientOverride";
                opt.PkiRoot = pkiRoot;
                opt.AutoAcceptUntrustedCertificates = true;
            });
            builder.ConfigureApplication(options => options.ApplicationName = "RootConfigured");

            ServiceProvider sp = services.BuildServiceProvider();
            try
            {
                OpcUaClientOptions resolved = sp.GetRequiredService<OpcUaClientOptions>();
                Assert.That(resolved.Configuration, Is.Not.Null);
                // Composition is order independent: the root value still wins even
                // though ConfigureApplication was registered after AddClient.
                Assert.That(resolved.Configuration!.ApplicationName, Is.EqualTo("RootConfigured"));
                Assert.That(
                    resolved.Configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                    Is.True);
            }
            finally
            {
                await sp.DisposeAsync().ConfigureAwait(false);
                DeletePkiRoot(pkiRoot);
            }
        }

        [Test]
        public async Task ConfigureApplicationSuppliesConfigurationBeforeConnectAsync()
        {
            ApplicationConfiguration configuration = CreateConfig();
            var configurationProvider = new TrackingConfigurationProvider(configuration);
            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IClientChannelManager>());
            services.AddSingleton<IOpcUaApplicationConfigurationProvider>(
                configurationProvider);
            services.AddOpcUa()
                .ConfigureApplication(options => options.ApplicationName = "ConfiguredClient")
                .AddClient(_ => { });

            ServiceProvider sp = services.BuildServiceProvider();
            try
            {
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                Assert.That(options.Configuration, Is.SameAs(configuration));

                IManagedSessionFactory factory =
                    sp.GetRequiredService<IManagedSessionFactory>();
                OperationCanceledException? exception =
                    Assert.CatchAsync<OperationCanceledException>(async () =>
                        await factory.ConnectAsync(
                            CreateEndpoint(),
                            _ => throw new OperationCanceledException(),
                            CancellationToken.None).ConfigureAwait(false));

                Assert.That(exception, Is.Not.Null);
                Assert.That(configurationProvider.GetCount, Is.EqualTo(1));
            }
            finally
            {
                await sp.DisposeAsync().ConfigureAwait(false);
            }
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
        public void AddSubscriptionsConfiguresSubscriptionOptions()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(opt => opt.Configuration = CreateConfig())
                .AddSubscriptions(subscriptionOptions =>
                {
                    subscriptionOptions.Disabled = true;
                    subscriptionOptions.PublishingEnabled = true;
                    subscriptionOptions.SendInitialValuesOnTransfer = true;
                });

            using ServiceProvider sp = services.BuildServiceProvider();
            Subscriptions.SubscriptionOptions resolvedOptions = sp
                .GetRequiredService<IOptionsMonitor<Subscriptions.SubscriptionOptions>>()
                .CurrentValue;

            Assert.That(resolvedOptions.Disabled, Is.True);
            Assert.That(resolvedOptions.PublishingEnabled, Is.True);
            Assert.That(resolvedOptions.SendInitialValuesOnTransfer, Is.True);
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
        public async Task ManagedSessionPoolKeepsDisconnectedSessionIdentityAsync()
        {
            await using Client.ManagedSession session = CreateUnconnectedManagedSession();
            session.StateMachine.Start();
            var factory = new Mock<IManagedSessionFactory>();
            int attempts = 0;
            factory.Setup(value => value.ConnectAsync(
                    It.IsAny<ConfiguredEndpoint>(), It.IsAny<Action<ManagedSessionBuilder>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++attempts == 1
                    ? session
                    : throw new InvalidOperationException("A disconnected session must not be replaced."));
            await using var pool = new ManagedSessionPool(factory.Object);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            Client.ManagedSession first = await pool.GetOrConnectAsync("primary", CreateEndpoint(), timeout.Token)
                .ConfigureAwait(false);
            Client.ManagedSession second = await pool.GetOrConnectAsync("primary", CreateEndpoint(), timeout.Token)
                .ConfigureAwait(false);

            Assert.That(first, Is.SameAs(session));
            Assert.That(second, Is.SameAs(session));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public async Task ManagedSessionPoolRejectsClosedFactoryResultWithoutRetryAsync()
        {
            await using Client.ManagedSession session = CreateUnconnectedManagedSession();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            session.StateMachine.Start();
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            var factory = new Mock<IManagedSessionFactory>();
            int attempts = 0;
            factory.Setup(value => value.ConnectAsync(
                    It.IsAny<ConfiguredEndpoint>(), It.IsAny<Action<ManagedSessionBuilder>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ++attempts <= 2
                    ? session
                    : throw new InvalidOperationException("The pool retried a closed factory result."));
            await using var pool = new ManagedSessionPool(factory.Object);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await pool.GetOrConnectAsync("primary", CreateEndpoint(), timeout.Token).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(attempts, Is.EqualTo(1));
        }

        [Test]
        public async Task ManagedSessionPoolReplacesExplicitlyClosedSessionOnceAsync()
        {
            await using Client.ManagedSession first = CreateUnconnectedManagedSession();
            await using Client.ManagedSession replacement = CreateUnconnectedManagedSession();
            first.StateMachine.Start();
            replacement.StateMachine.Start();
            var factory = new Mock<IManagedSessionFactory>();
            factory.SetupSequence(value => value.ConnectAsync(
                    It.IsAny<ConfiguredEndpoint>(), It.IsAny<Action<ManagedSessionBuilder>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(first)
                .ReturnsAsync(replacement);
            await using var pool = new ManagedSessionPool(factory.Object);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Client.ManagedSession cached = await pool.GetOrConnectAsync(
                "primary", CreateEndpoint(), timeout.Token).ConfigureAwait(false);
            Assert.That(cached, Is.SameAs(first));

            await first.CloseAsync(timeout.Token).ConfigureAwait(false);
            Client.ManagedSession next = await pool.GetOrConnectAsync(
                "primary", CreateEndpoint(), timeout.Token).ConfigureAwait(false);

            Assert.That(next, Is.SameAs(replacement));
            Assert.That(first.Disposed, Is.True);
            factory.Verify(value => value.ConnectAsync(
                It.IsAny<ConfiguredEndpoint>(), It.IsAny<Action<ManagedSessionBuilder>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task ManagedSessionPoolRemovalBoundsIgnoringFactoryAndDisposesLateSessionAsync()
        {
            await using Client.ManagedSession late = CreateUnconnectedManagedSession();
            late.StateMachine.Start();
            var completion = new TaskCompletionSource<Client.ManagedSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new Mock<IManagedSessionFactory>();
            factory.Setup(value => value.ConnectAsync(
                    It.IsAny<ConfiguredEndpoint>(), It.IsAny<Action<ManagedSessionBuilder>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(completion.Task);
            await using var pool = new ManagedSessionPool(factory.Object);
            using var caller = new CancellationTokenSource();
            Task<Client.ManagedSession> waiting = pool.GetOrConnectAsync("primary", CreateEndpoint(), caller.Token);
            caller.Cancel();
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(),
                async () => await waiting.ConfigureAwait(false));
            Task<bool> removal = pool.RemoveAsync("primary").AsTask();
            try
            {
                Assert.That(await removal.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.True);
            }
            finally
            {
                completion.TrySetResult(late);
                await removal.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await late.StateMachine.WaitForClosedAsync(timeout.Token).ConfigureAwait(false);
            while (!late.Disposed)
            {
                await Task.Delay(10, timeout.Token).ConfigureAwait(false);
            }
            Assert.That(late.Disposed, Is.True);
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

        private static string CreatePkiRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                nameof(OpcUaClientBuilderTests),
                Guid.NewGuid().ToString("N"));
        }

        private static void DeletePkiRoot(string pkiRoot)
        {
            if (Directory.Exists(pkiRoot))
            {
                Directory.Delete(pkiRoot, recursive: true);
            }
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
                    typeof(bool),
                    typeof(NetworkRedundancyOptions),
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
                false,
                null,
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

        private sealed class TrackingConfigurationProvider :
            IOpcUaApplicationConfigurationProvider
        {
            public TrackingConfigurationProvider(ApplicationConfiguration configuration)
            {
                Configuration = configuration;
                Application = new ApplicationInstance(
                    configuration,
                    NUnitTelemetryContext.Create());
            }

            public IApplicationInstance Application { get; }

            public ApplicationConfiguration Configuration { get; }

            public int GetCount { get; private set; }

            public Task<ApplicationConfiguration> GetAsync(
                CancellationToken ct = default)
            {
                GetCount++;
                return Task.FromResult(Configuration);
            }

            public ValueTask DisposeAsync()
            {
                return Application.DisposeAsync();
            }
        }
    }
}
