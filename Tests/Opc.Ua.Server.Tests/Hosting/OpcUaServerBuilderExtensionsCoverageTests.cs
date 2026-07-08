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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Coverage tests that exercise the DI-registration paths on
    /// <see cref="OpcUaServerBuilderExtensions"/> that are not covered
    /// by the existing hosting-test suite.
    /// </summary>
    /// <remarks>
    /// Tests only verify that the correct service descriptors are deposited
    /// in the container or that options are projected correctly.
    /// No hosted service is started and no network is used.
    /// </remarks>
    [TestFixture]
    [Category("Hosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaServerBuilderExtensionsCoverageTests
    {
        private static IOpcUaServerBuilder CreateBuilder()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return services.AddOpcUa().AddServer(o =>
            {
                o.ApplicationName = "CoverageServer";
                o.ApplicationUri = "urn:localhost:CoverageServer";
                o.ProductUri = "urn:localhost:CoverageServer:Product";
            });
        }

        private static IConfiguration EmptyConfiguration()
        {
            var source = new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?>()
            };

            return new ConfigurationBuilder().Add(source).Build();
        }

        private static IConfiguration CreateConfiguration(IDictionary<string, string?> values)
        {
            var source = new MemoryConfigurationSource { InitialData = values };
            return new ConfigurationBuilder().Add(source).Build();
        }

        [Test]
        public void AddServerTServerRegistersActivatorFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer<CustomTestServer>(o =>
            {
                o.ApplicationName = "CustomTypeServer";
                o.ApplicationUri = "urn:test:CustomTypeServer";
                o.ProductUri = "urn:test:product";
            });

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(d =>
                    d.ServiceType == typeof(IOpcUaServerFactory) &&
                    d.ImplementationType == typeof(ActivatorOpcUaServerFactory<CustomTestServer>)));

            _ = new CustomTestServer(
                NUnitTelemetryContext.Create(isServer: true),
                TimeProvider.System);
        }

        [Test]
        public void AddServerTServerNullBuilderThrowsArgumentNullException()
        {
            IOpcUaBuilder? nullBuilder = null;

            Assert.That(
                () => nullBuilder!.AddServer<CustomTestServer>(o => { }),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddServerTServerNullConfigureThrowsArgumentNullException()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Action<OpcUaServerOptions>? nullConfigure = null;

            Assert.That(
                () => builder.AddServer<CustomTestServer>(nullConfigure!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddServerIConfigurationRegistersHostedService()
        {
            IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
            {
                ["OpcUa:Server:ApplicationName"] = "ConfigServer",
                ["OpcUa:Server:ApplicationUri"] = "urn:test:ConfigServer",
                ["OpcUa:Server:ProductUri"] = "urn:test:product"
            });
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa().AddServer(config);

            Assert.That(serverBuilder, Is.Not.Null);
            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IOpcUaServerFactory)));
        }

        [Test]
        public void AddServerIConfigurationNullThrowsArgumentNullException()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            IConfiguration? nullConfig = null;

            Assert.That(
                () => builder.AddServer(nullConfig!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddServerIConfigurationSectionRegistersHostedService()
        {
            IConfiguration root = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ApplicationName"] = "SectionServer",
                ["ApplicationUri"] = "urn:test:SectionServer",
                ["ProductUri"] = "urn:test:product"
            });
            IConfigurationSection section = root.GetSection(string.Empty);
            var services = new ServiceCollection();
            services.AddLogging();
            IOpcUaServerBuilder serverBuilder = services.AddOpcUa().AddServer(section);

            Assert.That(serverBuilder, Is.Not.Null);
            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IOpcUaServerFactory)));
        }

        [Test]
        public void AddServerIConfigurationSectionNullThrowsArgumentNullException()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            IConfigurationSection? nullSection = null;

            Assert.That(
                () => builder.AddServer(nullSection!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConfigureRolesActionRegistersRoleManagerService()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.ConfigureRoles(o => { });

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IRoleManager)));
        }

        [Test]
        public void ConfigureRolesConfigurationRegistersRoleManagerService()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.ConfigureRoles(EmptyConfiguration());

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IRoleManager)));
        }

        [Test]
        public void AddRoleManagerInstanceRegistersExactInstance()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            var roleManager = new RoleManager();
            builder.AddRoleManager(roleManager);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            IRoleManager resolved = sp.GetRequiredService<IRoleManager>();

            Assert.That(resolved, Is.SameAs(roleManager));
        }

        [Test]
        public void AddRoleManagerTypeRegistersImplementationType()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddRoleManager<RoleManager>();

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(d =>
                    d.ServiceType == typeof(IRoleManager) &&
                    d.ImplementationType == typeof(RoleManager)));
        }

        [Test]
        public void AddIdentityAuthenticatorTypeRegistersDescriptor()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddIdentityAuthenticator<StubUserTokenAuthenticator>();

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(StubUserTokenAuthenticator)));

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerIdentityAuthenticatorRegistration)));

            _ = new StubUserTokenAuthenticator();
        }

        [Test]
        public void AddIdentityAugmenterTypeRegistersAugmenterRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddIdentityAugmenter<StubIdentityAugmenter>();

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerIdentityAugmenterRegistration)));

            _ = new StubIdentityAugmenter();
        }

        [Test]
        public void AddIdentityAugmenterFactoryRegistersAugmenterRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddIdentityAugmenter(_ => Mock.Of<IIdentityAugmenter>());

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerIdentityAugmenterRegistration)));
        }

        [Test]
        public void AddIdentityAugmenterNullFactoryThrowsArgumentNullException()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            Func<IServiceProvider, IIdentityAugmenter>? nullFactory = null;

            Assert.That(
                () => builder.AddIdentityAugmenter(nullFactory!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void WithKeyCredentialPushNoConfigureRegistersSubject()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.WithKeyCredentialPush();

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(KeyCredentialPushSubject)));
        }

        [Test]
        public void WithKeyCredentialPushWithConfigureRegistersSubject()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.WithKeyCredentialPush(o => o.ConfigurationFolderPath = "/Server/Configuration/Keys");

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(KeyCredentialPushSubject)));
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsActionRegistersRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddDefaultIdentityAuthenticators(o => o.EnableAnonymous = true);

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerIdentityAuthenticatorRegistration)));
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsConfigRegistersRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
            {
                ["EnableAnonymous"] = "true"
            });
            builder.AddDefaultIdentityAuthenticators(config);

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerIdentityAuthenticatorRegistration)));
        }

        [Test]
        public void AddJwtIssuerActionRegistersIssuerKeyResolver()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddJwtIssuer(o =>
            {
                o.IssuerUri = "https://issuer.example.test";
                o.JwksUri = "https://issuer.example.test/.well-known/jwks";
            });

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IIssuerKeyResolver)));
        }

        [Test]
        public void AddJwtIssuerConfigurationRegistersIssuerKeyResolver()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
            {
                ["IssuerUri"] = "https://issuer.example.test",
                ["JwksUri"] = "https://issuer.example.test/.well-known/jwks"
            });
            builder.AddJwtIssuer(config);

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IIssuerKeyResolver)));
        }

        [Test]
        public void AddDurableSubscriptionsRegistersStoreAndFactory()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            ISubscriptionStore store = Mock.Of<ISubscriptionStore>();
            IMonitoredItemQueueFactory queueFactory = Mock.Of<IMonitoredItemQueueFactory>();
            builder.AddDurableSubscriptions(store, queueFactory);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<ISubscriptionStore>(), Is.SameAs(store));
            Assert.That(sp.GetRequiredService<IMonitoredItemQueueFactory>(), Is.SameAs(queueFactory));
        }

        [Test]
        public void AddDurableSubscriptionsNullStoreThrowsArgumentNullException()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            ISubscriptionStore? nullStore = null;

            Assert.That(
                () => builder.AddDurableSubscriptions(nullStore!, Mock.Of<IMonitoredItemQueueFactory>()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddDurableSubscriptionsNullFactoryThrowsArgumentNullException()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IMonitoredItemQueueFactory? nullFactory = null;

            Assert.That(
                () => builder.AddDurableSubscriptions(Mock.Of<ISubscriptionStore>(), nullFactory!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddSessionManagerFactoryRegistersRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddSessionManager((_, _, _) => Mock.Of<ISessionManager>());

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerSessionManagerRegistration)));
        }

        [Test]
        public void AddSessionManagerTypeRegistersRegistrationAndTransient()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddSessionManager<SessionManager>();

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerSessionManagerRegistration)));

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(SessionManager)));
        }

        [Test]
        public void AddSubscriptionManagerFactoryRegistersRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddSubscriptionManager((_, _, _) => Mock.Of<ISubscriptionManager>());

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerSubscriptionManagerRegistration)));
        }

        [Test]
        public void AddSubscriptionManagerTypeRegistersRegistrationAndTransient()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddSubscriptionManager<SubscriptionManager>();

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerSubscriptionManagerRegistration)));

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(SubscriptionManager)));
        }

        [Test]
        public void AddHistorianRegistersHistorianProvider()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IHistorianProvider provider = Mock.Of<IHistorianProvider>();
            builder.AddHistorian(provider);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IHistorianProvider>(), Is.SameAs(provider));
        }

        [Test]
        public void AddFileSystemProviderRegistersNodeManagerFactory()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IFileSystemProvider provider = Mock.Of<IFileSystemProvider>();
            builder.AddFileSystem(provider);

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(FileSystemNodeManagerFactory)));
        }

        [Test]
        public void AddFileSystemDirectoryRegistersPhysicalProvider()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddFileSystem(".", "TestMount", isWritable: false);

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(FileSystemNodeManagerFactory)));
        }

        [Test]
        public void AddNodeManagerRegistersFactoryAndRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddNodeManager("http://test.org/UA/TestNs/", _ => { });

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IAsyncNodeManagerFactory)));

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerNodeManagerRegistration)));
        }

        [Test]
        public void AddSecretStoreRegistersStore()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            ISecretStore store = Mock.Of<ISecretStore>();
            builder.AddSecretStore(store);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<ISecretStore>(), Is.SameAs(store));
        }

        [Test]
        public void AddCertificateManagerRegistersCertificateManager()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            ICertificateManager certManager = Mock.Of<ICertificateManager>();
            builder.AddCertificateManager(certManager);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<ICertificateManager>(), Is.SameAs(certManager));
        }

        [Test]
        public void AddAliasNameStoreRegistersStoreAndRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IAliasNameStore aliasStore = Mock.Of<IAliasNameStore>();
            builder.AddAliasNameStore(aliasStore);

            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(OpcUaServerAliasNameStoreRegistration)));
        }

        [Test]
        public void AddAliasNameStoreRegistryRegistersRegistry()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IAliasNameStoreRegistry registry = Mock.Of<IAliasNameStoreRegistry>();
            builder.AddAliasNameStoreRegistry(registry);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IAliasNameStoreRegistry>(), Is.SameAs(registry));
        }

        [Test]
        public void AddReverseConnectActionReturnsBuilder()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IOpcUaServerBuilder result = builder.AddReverseConnect(o => o.ConnectIntervalMs = 3000);

            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void AddReverseConnectConfigurationReturnsBuilder()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectIntervalMs"] = "3000"
            });
            IOpcUaServerBuilder result = builder.AddReverseConnect(config);

            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void ConfigureOperationLimitsActionReturnsBuilder()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IOpcUaServerBuilder result = builder.ConfigureOperationLimits(
                o => o.MaxNodesPerRead = 500);

            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void ConfigureOperationLimitsConfigurationReturnsBuilder()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
            {
                ["MaxNodesPerRead"] = "500"
            });
            IOpcUaServerBuilder result = builder.ConfigureOperationLimits(config);

            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public void AddReferenceServerRegistersServerFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddReferenceServer();

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IOpcUaServerFactory)));
        }

        [Test]
        public void AddReferenceServerWithConfigureAppliesExtraOptions()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddReferenceServer(o =>
            {
                o.EndpointUrls.Clear();
                o.EndpointUrls.Add("opc.tcp://localhost:0/ReferenceServer");
            });

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IOpcUaServerFactory)));
        }

        [Test]
        public void AddSecureServerRegistersServerFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddSecureServer(o =>
            {
                o.ApplicationName = "SecureServer";
                o.ApplicationUri = "urn:localhost:SecureServer";
                o.ProductUri = "urn:localhost:SecureServer:Product";
            });

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IOpcUaServerFactory)));
        }

        [Test]
        public void AddHistorianFileStoreRegistersHistorianAndFileSystem()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            IHistorianProvider historian = Mock.Of<IHistorianProvider>();
            builder.AddHistorianFileStore(historian, ".", "TestMount", isWritable: false);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IHistorianProvider>(), Is.SameAs(historian));
            Assert.That(
                builder.Services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(FileSystemNodeManagerFactory)));
        }

        [Test]
        public void AddServerSectionWithRolesRegistersRoleManager()
        {
            IConfiguration config = CreateConfiguration(new Dictionary<string, string?>
            {
                ["OpcUa:Server:ApplicationName"] = "RolesServer",
                ["OpcUa:Server:ApplicationUri"] = "urn:test:RolesServer",
                ["OpcUa:Server:ProductUri"] = "urn:test:product",
                ["OpcUa:Server:Roles:Roles:0:RoleName"] = "Observer"
            });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(config);

            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(
                    d => d.ServiceType == typeof(IRoleManager)));
        }

        // ── Private test doubles ─────────────────────────────────────────────

        private sealed class CustomTestServer : StandardServer
        {
            public CustomTestServer(ITelemetryContext telemetry, TimeProvider timeProvider)
                : base(telemetry, timeProvider)
            {
            }
        }

        private sealed class StubUserTokenAuthenticator : IUserTokenAuthenticator
        {
            /// <inheritdoc/>
            public UserTokenType TokenType => UserTokenType.Anonymous;

            /// <inheritdoc/>
            public string? IssuedTokenProfileUri => null;

            /// <inheritdoc/>
            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }
        }

        private sealed class StubIdentityAugmenter : IIdentityAugmenter
        {
            /// <inheritdoc/>
            public ValueTask<AuthenticationResult> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }
        }
    }
}
