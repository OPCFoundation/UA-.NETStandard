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
 *
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

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace Opc.Ua.Server.Tests.Hosting
{
    [TestFixture]
    [Category("Identity")]
    [Category("Hosting")]
    public class IdentityHostingTests
    {
        [Test]
        public void DefaultAuthenticatorsRespectFlagsAndAvailableDependencies()
        {
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Identity:Defaults:EnableAnonymous"] = "true",
                ["OpcUa:Server:Identity:Defaults:EnableUserNamePassword"] = "true",
                ["OpcUa:Server:Identity:Defaults:EnableX509"] = "true",
                ["OpcUa:Server:Identity:Defaults:EnableJwt"] = "false"
            });

            using ServiceProvider withoutDependencies = CreateServices(configuration).BuildServiceProvider();
            Assert.That(CreateAuthenticators(withoutDependencies), Has.Exactly(1).TypeOf<AnonymousAuthenticator>());

            ServiceCollection services = CreateServices(configuration);
            services.AddSingleton(Mock.Of<IUserDatabase>());
            services.AddSingleton(Mock.Of<IUserManagement>());
            services.AddSingleton(Mock.Of<ICertificateValidatorEx>());
            using ServiceProvider withDependencies = services.BuildServiceProvider();

            IList<IUserTokenAuthenticator> authenticators = CreateAuthenticators(withDependencies);
            Assert.That(authenticators, Has.Exactly(1).TypeOf<AnonymousAuthenticator>());
            Assert.That(authenticators, Has.Exactly(1).TypeOf<UserNamePasswordAuthenticator>());
            Assert.That(authenticators, Has.Exactly(1).TypeOf<X509Authenticator>());

            IConfiguration disabledConfiguration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Identity:Defaults:EnableAnonymous"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableUserNamePassword"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableX509"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableJwt"] = "false"
            });
            using ServiceProvider disabled = CreateServices(disabledConfiguration).BuildServiceProvider();
            Assert.That(CreateAuthenticators(disabled), Is.Empty);
        }

        [Test]
        public void ConfiguredJwtIssuerRegistersResolverAndAuthenticator()
        {
            using RSA rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Identity:Defaults:EnableAnonymous"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableUserNamePassword"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableX509"] = "false",
                ["OpcUa:Server:Identity:Defaults:EnableJwt"] = "true",
                ["OpcUa:Server:Identity:Defaults:ExpectedAudience"] = "urn:opcua:test-server",
                ["OpcUa:Server:Identity:Issuers:0:IssuerUri"] = "https://issuer.example.test",
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:Kid"] = "kid-rsa",
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:Algorithm"] = "RS256",
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:RsaModulus"] = Base64UrlEncode(parameters.Modulus),
                ["OpcUa:Server:Identity:Issuers:0:StaticKeys:0:RsaExponent"] = Base64UrlEncode(parameters.Exponent)
            });
            using ServiceProvider services = CreateServices(configuration).BuildServiceProvider();

            Assert.That(services.GetServices<IIssuerKeyResolver>().Count(), Is.EqualTo(1));
            Assert.That(CreateAuthenticators(services), Has.Exactly(1).TypeOf<JwtAuthenticator>());
        }

        [Test]
        public void RolesConfigurationFlowsToRoleManagerOptions()
        {
            IConfiguration configuration = CreateConfiguration(new Dictionary<string, string>
            {
                ["OpcUa:Server:Roles:LegacyRoleCriteriaMatchesGrantedRoles"] = "true"
            });
            using ServiceProvider services = CreateServices(configuration).BuildServiceProvider();

            RoleConfigurationOptions options = services.GetRequiredService<IOptions<RoleConfigurationOptions>>().Value;
            using var roleManager = new RoleManager(options);
            FieldInfo field = typeof(RoleManager).GetField(
                "m_options",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var storedOptions = (RoleConfigurationOptions)field.GetValue(roleManager);

            Assert.That(storedOptions.LegacyRoleCriteriaMatchesGrantedRoles, Is.True);
        }

        private static ServiceCollection CreateServices(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddServer(configuration);
            return services;
        }

        private static List<IUserTokenAuthenticator> CreateAuthenticators(IServiceProvider services)
        {
            var authenticators = new List<IUserTokenAuthenticator>();
            foreach (OpcUaServerIdentityAuthenticatorRegistration registration in
                services.GetServices<OpcUaServerIdentityAuthenticatorRegistration>())
            {
                authenticators.AddRange(registration.CreateAuthenticators(services, null));
            }
            return authenticators;
        }

        private static IConfiguration CreateConfiguration(IDictionary<string, string> values)
        {
            var source = new MemoryConfigurationSource { InitialData = values };
            var builder = new ConfigurationBuilder();
            builder.Sources.Add(source);
            return builder.Build();
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
