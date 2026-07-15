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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Roles;
using Opc.Ua.Client.WebApi;
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
    public sealed class ClientDiFluentApiTests
    {
        [Test]
        public void AddClientChainsAlarmsAndWebApiTransport()
        {
            var services = new ServiceCollection();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddClient(ConfigureValidClient)
                .AddAlarms()
                .AddWebApiTransportChannel(options => options.Encoding = WebApiEncoding.Verbose);

            Assert.That(builder.Services, Is.SameAs(services));
            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<AlarmClientFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<WebApiClientOptions>(), Is.Not.Null);
            Assert.That(sp.GetRequiredService<WebApiClientOptions>().Encoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void ClientBuilderFeatureExtensionsRejectNullBuilders()
        {
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddAlarms(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddWebApiTransportChannel(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddHistorian(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddRoleManagement(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddFileTransfer(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddAliasNames(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDiscoveryRegistersDiscoveryServiceAndRejectsNullBuilder()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => ((IOpcUaBuilder)null!).AddDiscovery(),
                Throws.ArgumentNullException);

            services.AddOpcUa()
                .AddClient(ConfigureValidClient)
                .AddDiscovery();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IManagedSessionFactory>(), Is.InstanceOf<DefaultManagedSessionFactory>());
            Assert.That(sp.GetService<IOpcUaDiscoveryService>(), Is.InstanceOf<OpcUaDiscoveryService>());
            Assert.That(sp.GetService<Func<CancellationToken, Task<Client.ManagedSession>>>(), Is.Not.Null);
        }

        [Test]
        public void OptionsValidatorRejectsMissingConfiguration()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(_ => { });

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            Assert.That(
                () => _ = options.Value,
                Throws.TypeOf<OptionsValidationException>()
                    .With.Property(nameof(OptionsValidationException.Failures))
                    .Some.Contains("OpcUaClientOptions.Configuration is required."));
        }

        [Test]
        public void OptionsValidatorPassesWithConfigurationOnly()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options => options.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            Assert.That(() => _ = options.Value, Throws.Nothing);
            Assert.That(options.Value.Configuration, Is.Not.Null);
            Assert.That(options.Value.Session.Endpoint, Is.Null);
        }

        [Test]
        public void OptionsValidatorPassesWithConfigurationAndEndpoint()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(ConfigureValidClient);

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            Assert.That(options.Value.Configuration, Is.Not.Null);
            Assert.That(options.Value.Session.Endpoint, Is.Not.Null);
        }

        [Test]
        public void ConfigureApplicationBuildsClientConfiguration()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "ConfiguredClient";
                options.ApplicationUri = "urn:test:configured-client";
                options.ConfigureApplication(application =>
                {
                    application
                        .AddSecurityConfiguration(
                            ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                                "CN=ConfiguredClient, O=OPC Foundation, DC=localhost"))
                        .SetAutoAcceptUntrustedCertificates(true);
                });
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            Assert.That(options.Value.Configuration, Is.Not.Null);
            Assert.That(options.Value.Configuration!.ApplicationName, Is.EqualTo("ConfiguredClient"));
            Assert.That(options.Value.Configuration.ApplicationUri, Is.EqualTo("urn:test:configured-client"));
            Assert.That(options.Value.Configuration.ClientConfiguration, Is.Not.Null);
            Assert.That(options.Value.Configuration.SecurityConfiguration, Is.Not.Null);
            Assert.That(
                options.Value.Configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                Is.True);
        }

        [Test]
        public void ConfigureApplicationRequiresApplicationUri()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options => options.ConfigureApplication(_ => { }));

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
                () => _ = options.Value)!;

            Assert.That(
                ex.Failures,
                Does.Contain(
                    "OpcUaClientOptions.ApplicationUri is required when ConfigureApplication(...) is used."));
        }

        [Test]
        public void ConfigureApplicationRequiresSecurityConfiguration()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationUri = "urn:test:configured-client";
                options.ConfigureApplication(_ => { });
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
                () => _ = options.Value)!;

            Assert.That(
                ex.Failures,
                Does.Contain(
                    "ConfigureApplication(...) must add a security configuration."));
        }

        [Test]
        public void BuildServiceProviderAllowsClientWithoutStaticEndpoint()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddClient(options => options.Configuration = CreateConfig())
                .AddDiscovery();

            using ServiceProvider sp = services.BuildServiceProvider();
            IOptions<OpcUaClientOptions> options = sp.GetRequiredService<IOptions<OpcUaClientOptions>>();

            Assert.That(() => _ = options.Value, Throws.Nothing);
            Assert.That(sp.GetService<IManagedSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<IOpcUaDiscoveryService>(), Is.Not.Null);
        }

        [Test]
        public void ManagedSessionOptionsLoadComplexTypesDefaultsToFalse()
        {
            Assert.That(new ManagedSessionOptions().LoadComplexTypes, Is.False);
        }

        [Test]
        public void ManagedSessionBuilderWithLoadComplexTypesSetsOption()
        {
            ManagedSessionOptions options = new ManagedSessionBuilder(
                    CreateConfig(),
                    NUnitTelemetryContext.Create())
                .UseEndpoint(CreateEndpoint())
                .WithLoadComplexTypes()
                .Build();

            Assert.That(options.LoadComplexTypes, Is.True);
        }

        [Test]
        public async Task ManagedSessionFactoryUsesSuppliedEndpointsAsync()
        {
            var connector = new RecordingManagedSessionConnector();
            var services = new ServiceCollection();
            services.AddSingleton<IManagedSessionConnector>(connector);
            services.AddOpcUa().AddClient(options =>
            {
                options.Configuration = CreateConfig();
                options.Session = new ManagedSessionOptions
                {
                    SessionName = "ConfiguredSession",
                    LoadComplexTypes = true
                };
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            ConfiguredEndpoint firstEndpoint = CreateEndpoint("opc.tcp://first:4840");
            ConfiguredEndpoint secondEndpoint = CreateEndpoint("opc.tcp://second:4840");

            Client.ManagedSession first = await factory.ConnectAsync(firstEndpoint).ConfigureAwait(false);
            Client.ManagedSession second = await factory.ConnectAsync(secondEndpoint).ConfigureAwait(false);

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(connector.Options, Has.Count.EqualTo(2));
            Assert.That(connector.Options[0].Endpoint, Is.SameAs(firstEndpoint));
            Assert.That(connector.Options[1].Endpoint, Is.SameAs(secondEndpoint));
            Assert.That(connector.Options[0].SessionName, Is.EqualTo("ConfiguredSession"));
            Assert.That(connector.Options[0].LoadComplexTypes, Is.True);
        }

        [Test]
        public async Task ManagedSessionFactoryRuntimeEndpointDoesNotFailOptionsValidationAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options => options.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? ex = Assert.CatchAsync(
                async () => await factory
                    .ConnectAsync(CreateEndpoint("opc.tcp://127.0.0.1:9"), cts.Token)
                    .ConfigureAwait(false));

            Assert.That(
                IsEndpointRequiredOptionsException(ex),
                Is.False);
        }

        [Test]
        public void CachedSessionAccessorRequiresStaticEndpoint()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddClient(options => options.Configuration = CreateConfig());

            using ServiceProvider sp = services.BuildServiceProvider();
            Func<CancellationToken, Task<Client.ManagedSession>> factory = sp.GetRequiredService<Func<CancellationToken, Task<Client.ManagedSession>>>();

            OptionsValidationException ex = Assert.ThrowsAsync<OptionsValidationException>(
                () => factory(CancellationToken.None))!;

            Assert.That(ex.Failures, Does.Contain("A session endpoint is required."));
        }

        [Test]
        public void AddHistorianRegistersFactoryAndCreatesClient()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddHistorian();

            using ServiceProvider sp = services.BuildServiceProvider();
            ISession session = CreateSession();
            HistoryClientFactory factory = sp.GetRequiredService<HistoryClientFactory>();

            Assert.That(factory.Create(session).Session, Is.SameAs(session));
        }

        [Test]
        public void AddRoleManagementRegistersFactoryAndCreatesClient()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddRoleManagement();

            using ServiceProvider sp = services.BuildServiceProvider();
            ISession session = CreateSession();
            RoleManagementClientFactory factory = sp.GetRequiredService<RoleManagementClientFactory>();

            Assert.That(factory.Create(session).Session, Is.SameAs(session));
        }

        [Test]
        public void AddFileTransferRegistersFactoryAndCreatesClients()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddFileTransfer();

            using ServiceProvider sp = services.BuildServiceProvider();
            ISession session = CreateSession();
            FileTransferClientFactory factory = sp.GetRequiredService<FileTransferClientFactory>();

            Assert.That(factory.OpenServerFileSystem(session).Session, Is.SameAs(session));
            Assert.That(
                factory.CreateTemporaryFileTransfer(session, ObjectIds.FileSystem).Session,
                Is.SameAs(session));
        }

        [Test]
        public void AddAliasNamesRegistersFactoryAndCreatesClient()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddAliasNames();

            using ServiceProvider sp = services.BuildServiceProvider();
            ISession session = CreateSession();
            AliasNameClientFactory factory = sp.GetRequiredService<AliasNameClientFactory>();

            Assert.That(factory.OpenStandardAliases(session).Session, Is.SameAs(session));
        }

        [Test]
        public void SubClientFactoriesRejectNullSessions()
        {
            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddHistorian()
                .AddRoleManagement()
                .AddFileTransfer()
                .AddAliasNames();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(
                () => sp.GetRequiredService<HistoryClientFactory>().Create(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => sp.GetRequiredService<RoleManagementClientFactory>().Create(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => sp.GetRequiredService<FileTransferClientFactory>().OpenServerFileSystem(null!),
                Throws.ArgumentNullException);
            Assert.That(
                () => sp.GetRequiredService<AliasNameClientFactory>().OpenStandardAliases(null!),
                Throws.ArgumentNullException);
        }

        private static void ConfigureValidClient(OpcUaClientOptions options)
        {
            options.Configuration = CreateConfig();
            options.Session = new ManagedSessionOptions
            {
                Endpoint = CreateEndpoint()
            };
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return CreateEndpoint("opc.tcp://localhost:4840");
        }

        private static ConfiguredEndpoint CreateEndpoint(string endpointUrl)
        {
            return new ConfiguredEndpoint(
                null,
                new EndpointDescription
                {
                    EndpointUrl = endpointUrl
                },
                null);
        }

        private static ApplicationConfiguration CreateConfig()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client-di",
                ApplicationName = "client-di",
                ClientConfiguration = new ClientConfiguration()
            };
        }

        private static ISession CreateSession()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var session = new Mock<ISession>(MockBehavior.Loose);
            session.SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(telemetry));
            session.SetupGet(s => s.NamespaceUris)
                .Returns(new NamespaceTable());
            return session.Object;
        }

        private static bool IsEndpointRequiredOptionsException(Exception? ex)
        {
            return ex is OptionsValidationException optionsException &&
                optionsException.Failures.Contains("A session endpoint is required.");
        }

        private static Client.ManagedSession CreateManagedSession(ConfiguredEndpoint endpoint)
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
                endpoint,
                new DefaultSessionFactory(telemetry),
                new ReconnectPolicy(new ReconnectPolicyOptions()),
                null,
                telemetry.CreateLogger<Client.ManagedSession>(),
                null,
                null,
                TimeProvider.System,
                default(ArrayOf<string>),
                "TestSession",
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

        private sealed class RecordingManagedSessionConnector : IManagedSessionConnector
        {
            public List<ManagedSessionOptions> Options { get; } = [];

            public Task<Client.ManagedSession> ConnectAsync(
                IServiceProvider serviceProvider,
                ManagedSessionOptions sessionOptions,
                Action<ManagedSessionBuilder> configure,
                CancellationToken ct)
            {
                Options.Add(sessionOptions);
                return Task.FromResult(CreateManagedSession(sessionOptions.Endpoint!));
            }
        }
    }
}
