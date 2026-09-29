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
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Verifies the DI registration surface exposed by
    /// <c>OpcUaGdsServerBuilderExtensions.AddGdsServer(...)</c>. Does
    /// not start the hosted service.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("Builder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaGdsServerBuilderTests
    {
        private static readonly UserTokenType[] s_anonymousAndUserName =
            [UserTokenType.Anonymous, UserTokenType.UserName];

        [Test]
        public void AddGdsServerThrowsForNullArgs()
        {
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddGdsServer(
                    null, _ => { }),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddGdsServer((Action<GdsServerOptions>)null),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddGdsServer((IConfiguration)null),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddGdsServer((IConfigurationSection)null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddGdsServerRegistersExpectedServices()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(opt =>
            {
                opt.ApplicationName = "TestGds";
                opt.ApplicationUri = "urn:test:gds";
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IOptions<GdsServerOptions>>(), Is.Not.Null);
            Assert.That(sp.GetService<ITelemetryContext>(), Is.Not.Null);
            Assert.That(sp.GetService<IApplicationInstanceFactory>(), Is.Not.Null);

            ServiceDescriptor hostedDescriptor = services.FirstOrDefault(
                s => s.ImplementationType == typeof(GdsServerHostedService));
            Assert.That(hostedDescriptor, Is.Not.Null);
            Assert.That(hostedDescriptor.ServiceType, Is.EqualTo(typeof(IHostedService)));
        }

        [Test]
        public void AddGdsServerReturnsBuilderWithServices()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(opt => opt.ApplicationName = "TestGds");

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));
        }

        [Test]
        public void AddInMemoryStoresRegistersBuiltInStores()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));

            services.AddOpcUa()
                .AddGdsServer(opt => opt.ApplicationName = "TestGds")
                .AddInMemoryStores();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IApplicationsDatabase>(), Is.InstanceOf<LinqApplicationsDatabase>());
            Assert.That(sp.GetRequiredService<ICertificateRequest>(), Is.SameAs(
                sp.GetRequiredService<IApplicationsDatabase>()));
            Assert.That(sp.GetRequiredService<ICertificateGroup>(), Is.InstanceOf<CertificateGroup>());
            Assert.That(sp.GetRequiredService<IUserDatabase>(), Is.InstanceOf<LinqUserDatabase>());
        }

        [Test]
        public void AddInMemoryGdsServerRegistersPreset()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));

            services.AddOpcUa().AddInMemoryGdsServer(opt => opt.ApplicationName = "TestGds");

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetRequiredService<IApplicationsDatabase>(), Is.InstanceOf<LinqApplicationsDatabase>());
            Assert.That(sp.GetRequiredService<IUserDatabase>(), Is.InstanceOf<LinqUserDatabase>());
        }

        [Test]
        public void GdsBuilderTransportForwardersReturnSameBuilder()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(opt => opt.ApplicationName = "TestGds");

            Assert.That(builder.AddOpcTcpTransport(), Is.SameAs(builder));
            Assert.That(builder.AddHttpsTransport(), Is.SameAs(builder));
            Assert.That(builder.AddWssTransport(), Is.SameAs(builder));
        }

        [Test]
        public void GdsBuilderReverseConnectConfiguresOptions()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddGdsServer(opt => opt.ApplicationName = "TestGds")
                .AddReverseConnect(opt => opt.Clients.Add(new Ua.Server.Hosting.ServerReverseConnectClientOptions
                {
                    EndpointUrl = "opc.tcp://localhost:4841"
                }));

            using ServiceProvider sp = services.BuildServiceProvider();

            GdsServerOptions options = sp.GetRequiredService<IOptions<GdsServerOptions>>().Value;
            Assert.That(options.ReverseConnect, Is.Not.Null);
            Assert.That(options.ReverseConnect!.Clients, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddGdsServerFastFailsWhenStoresAreMissing()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));

            services.AddOpcUa().AddGdsServer(opt => opt.ApplicationName = "TestGds");

            using ServiceProvider sp = services.BuildServiceProvider();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => sp.GetServices<IHostedService>().Single())!;

            Assert.That(exception.Message, Does.Contain(nameof(IApplicationsDatabase)));
            Assert.That(exception.Message, Does.Contain("AddInMemoryStores"));
        }

        [Test]
        public void AddGdsServerThrowsOnDuplicateRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();

            builder.AddGdsServer(opt => opt.ApplicationName = "First");

            Assert.That(
                () => builder.AddGdsServer(opt => opt.ApplicationName = "Second"),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddAuthorizationServiceRegistersTokenServices()
        {
            var services = new ServiceCollection();

            services.AddOpcUa()
                .AddGdsServer(opt => opt.ApplicationName = "TestGds")
                .AddAuthorizationService(options =>
                {
                    options.IssuerUri = "urn:test:gds";
                    options.AllowedAudiences.Add("urn:test:gds:audience");
                });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<AuthorizationServiceManager>(), Is.Not.Null);
            Assert.That(sp.GetService<IAccessTokenProvider>(), Is.Not.Null);
        }

        [Test]
        public void AddGdsServerWithConfigurationSectionBindsOptions()
        {
            var configData = new Dictionary<string, string>
            {
                ["OpcUa:Gds:Server:ApplicationName"] = "BoundGds",
                ["OpcUa:Gds:Server:ApplicationUri"] = "urn:test:bound:gds",
                ["OpcUa:Gds:Server:AutoApprove"] = "false"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            GdsServerOptions options = sp.GetRequiredService<IOptions<GdsServerOptions>>().Value;

            Assert.That(options.ApplicationName, Is.EqualTo("BoundGds"));
            Assert.That(options.ApplicationUri, Is.EqualTo("urn:test:bound:gds"));
            Assert.That(options.AutoApprove, Is.False);
        }

        [Test]
        public void AddGdsServerWithConfigurationSectionBindsUserTokenPolicies()
        {
            var configData = new Dictionary<string, string>
            {
                ["OpcUa:Gds:Server:UserTokenPolicies:0:TokenType"] = "Anonymous",
                ["OpcUa:Gds:Server:UserTokenPolicies:1:TokenType"] = "UserName"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            GdsServerOptions options = sp.GetRequiredService<IOptions<GdsServerOptions>>().Value;

            Assert.That(
                options.UserTokenPolicies.Select(policy => policy.TokenType).ToArray(),
                Is.EqualTo(s_anonymousAndUserName));
        }

        [Test]
        public void AddStartupTasksAreIdempotentPerTaskType()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(o => o.ApplicationName = "Gds")
                .AddStartupTask<NoOpStartupTask>()
                .AddStartupTask<NoOpStartupTask>()
                .AddPreStartupTask<NoOpPreStartupTask>()
                .AddPreStartupTask<NoOpPreStartupTask>();

            using ServiceProvider sp = services.BuildServiceProvider();
            GdsServerStartupTaskRegistration startup =
                sp.GetServices<GdsServerStartupTaskRegistration>().Single();
            GdsServerPreStartupTaskRegistration preStartup =
                sp.GetServices<GdsServerPreStartupTaskRegistration>().Single();

            Assert.That(startup.TaskType, Is.EqualTo(typeof(NoOpStartupTask)));
            Assert.That(startup.Factory(sp), Is.SameAs(sp.GetRequiredService<NoOpStartupTask>()));
            Assert.That(preStartup.Factory(sp), Is.SameAs(sp.GetRequiredService<NoOpPreStartupTask>()));
            Assert.That(
                () => ((IGdsServerBuilder)null).AddStartupTask<NoOpStartupTask>(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IGdsServerBuilder)null).AddPreStartupTask<NoOpPreStartupTask>(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsBindsGdsOptionsFromConfiguration()
        {
            IConfiguration section = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["EnableAnonymous"] = "false",
                    ["EnableX509"] = "false",
                    ["ExpectedAudience"] = "urn:audience",
                    ["ClockSkewTolerance"] = "00:02:00",
                    ["UserCertificateTrustList"] = "CustomUsers",
                    ["EnableGdsApplicationSelfAdminProvider"] = "false"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(o => o.ApplicationName = "Gds")
                .AddDefaultIdentityAuthenticators(section);

            using ServiceProvider sp = services.BuildServiceProvider();
            GdsDefaultIdentityAuthenticatorOptions options =
                GdsServerHostedService.GetDefaultAuthenticatorOptions(sp);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.EnableAnonymous, Is.False);
            Assert.That(options.EnableUserNamePassword, Is.True);
            Assert.That(options.EnableX509, Is.False);
            Assert.That(options.ExpectedAudience, Is.EqualTo("urn:audience"));
            Assert.That(options.ClockSkewTolerance, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(options.UserCertificateTrustList.Name, Is.EqualTo("CustomUsers"));
            Assert.That(options.EnableGdsApplicationSelfAdminProvider, Is.False);
        }

        public sealed class NoOpStartupTask : Opc.Ua.Server.Hosting.IServerStartupTask
        {
            public System.Threading.Tasks.ValueTask OnServerStartedAsync(
                Opc.Ua.Server.IServerContext server,
                System.Threading.CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        public sealed class NoOpPreStartupTask : Opc.Ua.Server.Hosting.IServerPreStartupTask
        {
            public System.Threading.Tasks.ValueTask OnServerStartingAsync(
                Opc.Ua.Server.IServerContext server,
                System.Threading.CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        [Test]
        public void AddGdsServerWithConfigurationSectionBindsCertificateGroups()
        {
            var configData = new Dictionary<string, string>
            {
                ["OpcUa:Gds:Server:CertificateGroups:0:Id"] = "Default",
                ["OpcUa:Gds:Server:CertificateGroups:0:CertificateTypes:0"] = "RsaSha256ApplicationCertificateType",
                ["OpcUa:Gds:Server:CertificateGroups:0:CertificateTypes:1"] = "EccNistP256ApplicationCertificateType",
                ["OpcUa:Gds:Server:CertificateGroups:0:SubjectName"] = "CN=Bound CA, O=OPC Foundation",
                ["OpcUa:Gds:Server:CertificateGroups:0:CACertificateLifetime"] = "120"
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var services = new ServiceCollection();
            services.AddOpcUa().AddGdsServer(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            GdsServerOptions options = sp.GetRequiredService<IOptions<GdsServerOptions>>().Value;
            GlobalDiscoveryServerConfiguration gdsConfiguration =
                GdsServerHostedService.BuildGdsConfiguration(options, "pki");

            Assert.That(gdsConfiguration.CertificateGroups.Count, Is.EqualTo(1));
            CertificateGroupConfiguration group = gdsConfiguration.CertificateGroups[0];
            Assert.That(group.Id, Is.EqualTo("Default"));
            Assert.That(group.CertificateTypes.Count, Is.EqualTo(2));
            Assert.That(group.CertificateTypes[0], Is.EqualTo("RsaSha256ApplicationCertificateType"));
            Assert.That(group.CertificateTypes[1], Is.EqualTo("EccNistP256ApplicationCertificateType"));
            Assert.That(group.SubjectName, Is.EqualTo("CN=Bound CA, O=OPC Foundation"));
            Assert.That(group.BaseStorePath, Is.EqualTo(System.IO.Path.Combine("pki", "CA", "Default")));
            Assert.That(group.CACertificateLifetime, Is.EqualTo((ushort)120));
        }

        [Test]
        public void DefaultCertificateGroupCaUsesResolvedApplicationName()
        {
            var options = new GdsServerOptions { ApplicationName = string.Empty };

            GlobalDiscoveryServerConfiguration configuration =
                GdsServerHostedService.BuildGdsConfiguration(options, "pki");

            Assert.That(configuration.CertificateGroups.Count, Is.EqualTo(1));
            Assert.That(
                configuration.CertificateGroups[0].SubjectName,
                Is.EqualTo("CN=GlobalDiscoveryServer CA, O=OPC Foundation"));
        }

        /// <summary>
        /// OPC 10000-12 §7.8.3.3: the DefaultApplicationGroup is mandatory, so
        /// configured groups without it get the default group appended.
        /// </summary>
        [Test]
        public void ConfiguredGroupsWithoutDefaultGetDefaultApplicationGroup()
        {
            var options = new GdsServerOptions { ApplicationName = "Gds" };
            var httpsGroup = new GdsCertificateGroupOptions
            {
                Id = "DefaultHttpsGroup",
                SubjectName = "CN=Https CA"
            };
            httpsGroup.CertificateTypes.Add("HttpsCertificateType");
            options.CertificateGroups.Add(httpsGroup);

            GlobalDiscoveryServerConfiguration configuration =
                GdsServerHostedService.BuildGdsConfiguration(options, "pki");

            Assert.That(configuration.CertificateGroups.Count, Is.EqualTo(2));
            Assert.That(configuration.CertificateGroups[0].Id, Is.EqualTo("DefaultHttpsGroup"));
            CertificateGroupConfiguration defaultGroup = configuration.CertificateGroups[1];
            Assert.That(defaultGroup.Id, Is.EqualTo("Default"));
            Assert.That(defaultGroup.SubjectName, Is.EqualTo("CN=Gds CA, O=OPC Foundation"));

            options.CertificateGroups[0].Id = "defaultapplicationgroup";
            configuration = GdsServerHostedService.BuildGdsConfiguration(options, "pki");
            Assert.That(configuration.CertificateGroups.Count, Is.EqualTo(1),
                "A configured DefaultApplicationGroup must not be duplicated.");
        }

        [Test]
        public void AddGdsServerCanCoexistWithAddServer()
        {
            var services = new ServiceCollection();
            Assert.That(
                () => services.AddOpcUa()
                    .AddServer(opt => opt.ApplicationName = "RegularServer")
                    .Services.AddOpcUa()
                    .AddGdsServer(opt => opt.ApplicationName = "GdsServer"),
                Throws.Nothing);

            using ServiceProvider sp = services.BuildServiceProvider();

            // Both server features published an options registration.
            Assert.That(
                sp.GetRequiredService<IOptions<Ua.Server.Hosting.OpcUaServerOptions>>().Value.ApplicationName,
                Is.EqualTo("RegularServer"));
            Assert.That(
                sp.GetRequiredService<IOptions<GdsServerOptions>>().Value.ApplicationName,
                Is.EqualTo("GdsServer"));

            // Both hosted services are registered.
            int hostedCount = services.Count(s =>
                s.ServiceType == typeof(IHostedService) &&
                (s.ImplementationType == typeof(GdsServerHostedService) ||
                    s.ImplementationType?.Name == "OpcUaServerHostedService"));
            Assert.That(hostedCount, Is.EqualTo(2));
        }
    }
}
