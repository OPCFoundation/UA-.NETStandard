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
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Gds.Tests.Hosting
{
    /// <summary>
    /// Verifies that GDS identity builder methods proxy to the regular server builder surface.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [Category("GdsHosting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class GdsIdentityForwardingTests
    {
        [Test]
        public void ConfigureRolesActionMatchesServerBuilderRegistration()
        {
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.ConfigureRoles(options => { }));
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.ConfigureRoles(options => { }));

            Assert.That(gdsDelta, Is.EqualTo(serverDelta));
        }

        [Test]
        public void ConfigureRolesActionRegistersOptions()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "Gds");

            IGdsServerBuilder returned = builder.ConfigureRoles(options => { });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(
                sp.GetRequiredService<IOptions<RoleConfigurationOptions>>().Value,
                Is.Not.Null);
        }

        [Test]
        public void AddIdentityAuthenticatorMatchesServerBuilderRegistration()
        {
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.AddIdentityAuthenticator<StubAuthenticator>());
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.AddIdentityAuthenticator<StubAuthenticator>());

            Assert.That(gdsDelta, Is.EqualTo(serverDelta));
        }

        [Test]
        public void AddIdentityAuthenticatorRegistersAuthenticatorType()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "Gds");

            IGdsServerBuilder returned = builder.AddIdentityAuthenticator<StubAuthenticator>();

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(sp.GetService<StubAuthenticator>(), Is.Not.Null);
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsActionMatchesServerBuilderRegistration()
        {
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.AddDefaultIdentityAuthenticators(options => options.EnableJwt = false));
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.AddDefaultIdentityAuthenticators(options => options.EnableJwt = false));

            AssertGdsDefaultIdentityAuthenticatorsDelta(gdsDelta, serverDelta);
        }

        [Test]
        public void ConfigureRolesConfigurationMatchesServerBuilderRegistration()
        {
            IConfiguration section = BuildConfiguration("Marker", "value");
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.ConfigureRoles(section));
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.ConfigureRoles(section));

            Assert.That(gdsDelta, Is.EqualTo(serverDelta));
        }

        [Test]
        public void ConfigureRolesConfigurationRegistersOptions()
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "Gds");
            IConfiguration section = BuildConfiguration("Marker", "value");

            IGdsServerBuilder returned = builder.ConfigureRoles(section);

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(
                sp.GetRequiredService<IOptions<RoleConfigurationOptions>>().Value,
                Is.Not.Null);
        }

        [Test]
        public void AddDefaultIdentityAuthenticatorsConfigurationMatchesServerBuilderRegistration()
        {
            IConfiguration section = BuildConfiguration("EnableJwt", "false");
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.AddDefaultIdentityAuthenticators(section));
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.AddDefaultIdentityAuthenticators(section));

            AssertGdsDefaultIdentityAuthenticatorsDelta(gdsDelta, serverDelta);
        }

        [Test]
        public void AddJwtIssuerActionMatchesServerBuilderRegistration()
        {
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.AddJwtIssuer(opt =>
                {
                    opt.IssuerUri = "https://issuer.example";
                    opt.JwksUri = "https://issuer.example/.well-known/jwks";
                }));
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.AddJwtIssuer(opt =>
                {
                    opt.IssuerUri = "https://issuer.example";
                    opt.JwksUri = "https://issuer.example/.well-known/jwks";
                }));

            Assert.That(gdsDelta, Is.EqualTo(serverDelta));
        }

        [Test]
        public void AddJwtIssuerConfigurationMatchesServerBuilderRegistration()
        {
            IConfiguration section = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["IssuerUri"] = "https://issuer.example",
                    ["JwksUri"] = "https://issuer.example/.well-known/jwks"
                })
                .Build();
            IReadOnlyList<string> serverDelta = CaptureServerDelta(builder =>
                builder.AddJwtIssuer(section));
            IReadOnlyList<string> gdsDelta = CaptureGdsDelta(builder =>
                builder.AddJwtIssuer(section));

            Assert.That(gdsDelta, Is.EqualTo(serverDelta));
        }

        private static IConfiguration BuildConfiguration(string key, string value)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { [key] = value })
                .Build();
        }

        private static void AssertGdsDefaultIdentityAuthenticatorsDelta(
            IReadOnlyList<string> gdsDelta,
            IReadOnlyList<string> serverDelta)
        {
            const string identityAugmenterRegistration =
                "Opc.Ua.Server.Hosting.OpcUaServerIdentityAugmenterRegistration";
            string selfAdminRegistration = string.Join(
                "|",
                ServiceLifetime.Singleton,
                identityAugmenterRegistration,
                string.Empty,
                string.Empty,
                identityAugmenterRegistration);

            const string selfAdminOptions =
                "Opc.Ua.Gds.Server.Hosting.GdsApplicationSelfAdminProviderOptions";

            Assert.That(gdsDelta.Take(serverDelta.Count), Is.EqualTo(serverDelta));
            Assert.That(gdsDelta.Skip(serverDelta.Count).ToArray(), Has.Length.EqualTo(2));
            Assert.That(
                gdsDelta[serverDelta.Count],
                Does.Contain("Microsoft.Extensions.Options.IConfigureOptions`1[[" + selfAdminOptions));
            Assert.That(
                gdsDelta[serverDelta.Count],
                Does.Contain("Microsoft.Extensions.Options.ConfigureNamedOptions`1[[" + selfAdminOptions));
            Assert.That(gdsDelta[serverDelta.Count + 1], Is.EqualTo(selfAdminRegistration));
        }

        private static string[] CaptureServerDelta(Action<IOpcUaServerBuilder> configure)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa()
                .AddServer(options => options.ApplicationName = "Server");
            int beforeCount = services.Count;

            configure(builder);

            return DescribeDelta(services, beforeCount);
        }

        private static string[] CaptureGdsDelta(Action<IGdsServerBuilder> configure)
        {
            var services = new ServiceCollection();
            IGdsServerBuilder builder = services.AddOpcUa()
                .AddGdsServer(options => options.ApplicationName = "Gds");
            int beforeCount = services.Count;

            configure(builder);

            return DescribeDelta(services, beforeCount);
        }

        private static string[] DescribeDelta(IServiceCollection services, int startIndex)
        {
            return [.. services.Skip(startIndex).Select(Describe)];
        }

        private static string Describe(ServiceDescriptor descriptor)
        {
            return string.Join(
                "|",
                descriptor.Lifetime,
                GetTypeName(descriptor.ServiceType),
                GetTypeName(descriptor.ImplementationType),
                descriptor.ImplementationFactory == null ? string.Empty : "factory",
                descriptor.ImplementationInstance == null
                    ? string.Empty
                    : GetTypeName(descriptor.ImplementationInstance.GetType()));
        }

        private static string GetTypeName(Type type)
        {
            return type?.FullName ?? string.Empty;
        }

        public sealed class StubAuthenticator : IUserTokenAuthenticator
        {
            public UserTokenType TokenType => UserTokenType.Anonymous;

            public string IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }
        }
    }
}
