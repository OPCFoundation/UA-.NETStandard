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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Verifies the transport, persistence-store and identity registration
    /// overloads of <c>OpcUaGdsServerBuilderExtensions</c> using pure DI
    /// registration assertions, without starting the hosted GDS server.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("Builder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class OpcUaGdsServerBuilderCoverageTests
    {
        [Test]
        public void AddHttpsTransportWithConfigureReturnsSameBuilder()
        {
            IGdsServerBuilder builder = CreateBuilder();

            Assert.That(builder.AddHttpsTransport(_ => { }), Is.SameAs(builder));
        }

        [Test]
        public void AddWssTransportWithConfigureReturnsSameBuilder()
        {
            IGdsServerBuilder builder = CreateBuilder();

            Assert.That(builder.AddWssTransport(_ => { }), Is.SameAs(builder));
        }

        [Test]
        public void TransportForwardersThrowForNullBuilder()
        {
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddOpcTcpTransport(null),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddHttpsTransport(null),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddHttpsTransport(null, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddWssTransport(null),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddWssTransport(null, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddReverseConnectThrowsForNullArgs()
        {
            IGdsServerBuilder builder = CreateBuilder();

            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddReverseConnect(null, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddReverseConnect(null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddApplicationsDatabaseGenericResolvesStore()
        {
            IServiceCollection services = CreateServicesWithStoreDependencies();
            services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds")
                .AddApplicationsDatabase<LinqApplicationsDatabase>();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<IApplicationsDatabase>(),
                Is.InstanceOf<LinqApplicationsDatabase>());
        }

        [Test]
        public void AddCertificateRequestGenericResolvesStore()
        {
            IServiceCollection services = CreateServicesWithStoreDependencies();
            services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds")
                .AddCertificateRequest<LinqApplicationsDatabase>();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ICertificateRequest>(),
                Is.InstanceOf<LinqApplicationsDatabase>());
        }

        [Test]
        public void AddCertificateGroupGenericResolvesStore()
        {
            IServiceCollection services = CreateServicesWithStoreDependencies();
            services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds")
                .AddCertificateGroup<CertificateGroup>();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ICertificateGroup>(),
                Is.InstanceOf<CertificateGroup>());
        }

        [Test]
        public void AddUserDatabaseGenericResolvesStore()
        {
            IServiceCollection services = CreateServicesWithStoreDependencies();
            services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds")
                .AddUserDatabase<LinqUserDatabase>();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<IUserDatabase>(),
                Is.InstanceOf<LinqUserDatabase>());
        }

        [Test]
        public void AddApplicationsDatabaseFactoryResolvesStore()
        {
            IServiceCollection services = CreateServicesWithStoreDependencies();
            services.AddSingleton<LinqApplicationsDatabase>();
            services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds")
                .AddApplicationsDatabase(sp => sp.GetRequiredService<LinqApplicationsDatabase>());

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<IApplicationsDatabase>(),
                Is.InstanceOf<LinqApplicationsDatabase>());
        }

        [Test]
        public void AddApplicationsDatabaseFactoryThrowsForNullFactory()
        {
            IGdsServerBuilder builder = CreateBuilder();

            Assert.That(
                () => builder.AddApplicationsDatabase(
                    null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddIdentityAugmenterFactoryRegistersAugmenter()
        {
            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration>(services);
            IGdsServerBuilder result = builder.AddIdentityAugmenter(_ => new TestIdentityHandler());

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration>(services),
                Is.GreaterThan(before));
        }

        [Test]
        public void AddIdentityAugmenterFactoryThrowsForNullArgs()
        {
            IGdsServerBuilder builder = CreateBuilder();

            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddIdentityAugmenter(
                    null, _ => new TestIdentityHandler()),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddIdentityAugmenter(
                    null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddIdentityAuthenticatorGenericRegistersAuthenticator()
        {
            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services);
            IGdsServerBuilder result = builder.AddIdentityAuthenticator<TestIdentityHandler>();

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(d => d.ServiceType == typeof(TestIdentityHandler)));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services),
                Is.GreaterThan(before));
        }

        [Test]
        public void AddIdentityAugmenterGenericRegistersAugmenter()
        {
            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration>(services);
            IGdsServerBuilder result = builder.AddIdentityAugmenter<TestIdentityHandler>();

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(d => d.ServiceType == typeof(TestIdentityHandler)));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration>(services),
                Is.GreaterThan(before));
        }

        [Test]
        public void GenericIdentityMethodsThrowForNullBuilder()
        {
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddIdentityAuthenticator<TestIdentityHandler>(null),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddIdentityAugmenter<TestIdentityHandler>(null),
                Throws.ArgumentNullException);
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddGdsApplicationSelfAdminProvider(null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddGdsApplicationSelfAdminProviderRegistersAugmenter()
        {
            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration>(services);
            IGdsServerBuilder result = builder.AddGdsApplicationSelfAdminProvider();

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration>(services),
                Is.GreaterThan(before));
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsWithConfigureRegistersAuthenticators()
        {
            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services);
            IGdsServerBuilder result = builder.AddDefaultIdentityAuthenticators(_ => { });

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services),
                Is.GreaterThan(before));
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsThrowsForNullArgs()
        {
            IGdsServerBuilder builder = CreateBuilder();

            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddDefaultIdentityAuthenticators(
                    null, _ => { }),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddDefaultIdentityAuthenticators(
                    (Action<GdsDefaultIdentityAuthenticatorOptions>)null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DisableGdsApplicationSelfAdminProviderRegistersAuthenticators()
        {
            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services);
            IGdsServerBuilder result = builder.DisableGdsApplicationSelfAdminProvider();

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services),
                Is.GreaterThan(before));
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsFromConfigurationRegistersAuthenticators()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["EnableGdsApplicationSelfAdminProvider"] = "false"
                })
                .Build();

            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            int before = CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services);
            IGdsServerBuilder result = builder.AddDefaultIdentityAuthenticators(configuration);

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                CountRegistrations<Ua.Server.Hosting.OpcUaServerIdentityAuthenticatorRegistration>(services),
                Is.GreaterThan(before));
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.AddDefaultIdentityAuthenticators(
                    null, configuration),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.AddDefaultIdentityAuthenticators((IConfiguration)null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void ConfigureRolesFromConfigurationRegistersRoleManager()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection([])
                .Build();

            IServiceCollection services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");

            IGdsServerBuilder result = builder.ConfigureRoles(configuration);

            Assert.That(result, Is.SameAs(builder));
            Assert.That(
                services,
                Has.Some.Matches<ServiceDescriptor>(d =>
                    d.ServiceType == typeof(Ua.Server.IRoleManager)));
            Assert.That(
                () => OpcUaGdsServerBuilderExtensions.ConfigureRoles(null, configuration),
                Throws.ArgumentNullException);
            Assert.That(
                () => builder.ConfigureRoles(null),
                Throws.ArgumentNullException);
        }

        private static IGdsServerBuilder CreateBuilder()
        {
            return new ServiceCollection()
                .AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "TestGds");
        }

        private static ServiceCollection CreateServicesWithStoreDependencies()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(NUnitTelemetryContext.Create(isServer: true));
            return services;
        }

        private static int CountRegistrations<T>(IServiceCollection services)
        {
            return services.Count(d => d.ServiceType == typeof(T));
        }

        private sealed class TestIdentityHandler : IIdentityAugmenter, IUserTokenAuthenticator
        {
            public UserTokenType TokenType => UserTokenType.Anonymous;

            public string? IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }

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
